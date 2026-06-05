using ClickHouse.Driver.ADO;
using InventoryEventMessage = OTelSample.ZeroCodeMassTransit.Messages.InventoryEvent;
using MassTransit;
using Microsoft.Extensions.Options;
using Npgsql;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

var postgresConnectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? "Host=postgres;Port=5432;Database=otel;Username=otel;Password=otel";
var clickHouseConnectionString = builder.Configuration.GetConnectionString("ClickHouse")
    ?? "Host=clickhouse;Protocol=http;Port=8123;Username=default;Password=";
var redisConnectionString = builder.Configuration.GetConnectionString("Redis")
    ?? "redis:6379";

builder.Services.AddSingleton(new NpgsqlDataSourceBuilder(postgresConnectionString).Build());
builder.Services.AddSingleton(new ClickHouseOptions(clickHouseConnectionString));
builder.Services.AddSingleton<ClickHouseInitializationState>();
builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConnectionString));
builder.Services.Configure<RabbitMqOptions>(builder.Configuration.GetSection("RabbitMq"));
builder.Services.AddOptions<MassTransitHostOptions>().Configure(options =>
{
    options.WaitUntilStarted = true;
    options.StartTimeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<InventoryEventConsumer>();
    x.SetKebabCaseEndpointNameFormatter();

    x.UsingRabbitMq((context, cfg) =>
    {
        var options = context.GetRequiredService<IOptions<RabbitMqOptions>>().Value;

        cfg.Host(options.Host, options.Port, "/", h =>
        {
            h.Username(options.Username);
            h.Password(options.Password);
        });

        cfg.ReceiveEndpoint(options.Queue, endpoint =>
        {
            endpoint.ConfigureConsumer<InventoryEventConsumer>(context);
        });
    });
});

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var dataSource = scope.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
    var clickHouseOptions = scope.ServiceProvider.GetRequiredService<ClickHouseOptions>();
    var redis = scope.ServiceProvider.GetRequiredService<IConnectionMultiplexer>();
    await WaitForDependencies(dataSource, clickHouseOptions.ConnectionString, redis, app.Logger);
}

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "inventory-service" }));

app.MapGet("/inventory/{id:int}", async (
    int id,
    NpgsqlDataSource dataSource,
    ClickHouseOptions clickHouseOptions,
    ClickHouseInitializationState clickHouseInitializationState,
    IConnectionMultiplexer redis,
    IPublishEndpoint publishEndpoint,
    CancellationToken cancellationToken) =>
{
    await EnsureClickHouseInitialized(
        clickHouseInitializationState,
        clickHouseOptions.ConnectionString,
        cancellationToken);

    var available = await GetInventoryLevel(dataSource, id, cancellationToken);
    var clickHouseCount = await RecordAndCountClickHouse(clickHouseOptions.ConnectionString, id, available, cancellationToken);
    var redisValue = await RecordAndReadRedis(redis, id, available);
    await PublishInventoryEvent(publishEndpoint, id, available, cancellationToken);

    return Results.Ok(new
    {
        itemId = id,
        available,
        clickHouseCount,
        redisValue,
        rabbitPublished = true
    });
});

app.Run();

static async Task WaitForDependencies(
    NpgsqlDataSource dataSource,
    string clickHouseConnectionString,
    IConnectionMultiplexer redis,
    ILogger logger)
{
    await Retry("PostgreSQL initialization", logger, () => InitializePostgres(dataSource));
    await Retry("ClickHouse initialization", logger, () => InitializeClickHouse(clickHouseConnectionString));
    await Retry("Redis initialization", logger, () => InitializeRedis(redis));
}

static async Task<int> GetInventoryLevel(NpgsqlDataSource dataSource, int id, CancellationToken cancellationToken)
{
    await using var command = dataSource.CreateCommand("SELECT available FROM inventory WHERE item_id = @id");
    command.Parameters.AddWithValue("id", id);

    var result = await command.ExecuteScalarAsync(cancellationToken);
    return result is DBNull or null ? 0 : Convert.ToInt32(result);
}

static async Task InitializePostgres(NpgsqlDataSource dataSource)
{
    const string createTable = """
        CREATE TABLE IF NOT EXISTS inventory (
            item_id INT PRIMARY KEY,
            available INT NOT NULL
        );
        """;

    const string seed = """
        INSERT INTO inventory (item_id, available)
        VALUES (1, 42), (2, 5), (3, 0)
        ON CONFLICT (item_id) DO UPDATE SET available = EXCLUDED.available;
        """;

    await using var create = dataSource.CreateCommand(createTable);
    await create.ExecuteNonQueryAsync();

    await using var insert = dataSource.CreateCommand(seed);
    await insert.ExecuteNonQueryAsync();
}

static async Task InitializeClickHouse(string connectionString)
{
    await using var connection = new ClickHouseConnection(connectionString);
    await connection.OpenAsync();

    await using var command = connection.CreateCommand();
    command.CommandText = """
        CREATE TABLE IF NOT EXISTS inventory_requests (
            ts DateTime('UTC'),
            item_id Int32,
            available Int32,
            source String
        ) ENGINE = MergeTree ORDER BY (item_id, ts)
        """;
    await command.ExecuteNonQueryAsync();
}

static async Task InitializeRedis(IConnectionMultiplexer redis)
{
    var db = redis.GetDatabase();
    await db.StringSetAsync("inventory:startup", "ready");
    await db.StringGetAsync("inventory:startup");
}

static async Task EnsureClickHouseInitialized(
    ClickHouseInitializationState state,
    string connectionString,
    CancellationToken cancellationToken)
{
    if (state.Initialized)
    {
        return;
    }

    await state.Lock.WaitAsync(cancellationToken);

    try
    {
        if (state.Initialized)
        {
            return;
        }

        await InitializeClickHouse(connectionString);
        state.Initialized = true;
    }
    finally
    {
        state.Lock.Release();
    }
}

static async Task Retry(
    string operation,
    ILogger logger,
    Func<Task> action,
    int maxAttempts = 30,
    int delaySeconds = 2)
{
    for (var attempt = 1; attempt <= maxAttempts; attempt++)
    {
        try
        {
            await action();
            logger.LogInformation("{Operation} completed on attempt {Attempt}", operation, attempt);
            return;
        }
        catch (Exception ex) when (attempt < maxAttempts)
        {
            logger.LogWarning(ex, "{Operation} failed on attempt {Attempt}; retrying in {DelaySeconds}s", operation, attempt, delaySeconds);
            await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
        }
    }

    await action();
}

static async Task<int> RecordAndCountClickHouse(string connectionString, int id, int available, CancellationToken cancellationToken)
{
    await using var connection = new ClickHouseConnection(connectionString);
    await connection.OpenAsync(cancellationToken);

    await using (var insert = connection.CreateCommand())
    {
        insert.CommandText = $"INSERT INTO inventory_requests (ts, item_id, available, source) VALUES (now(), {id}, {available}, 'inventory-http')";
        await insert.ExecuteNonQueryAsync(cancellationToken);
    }

    await using var count = connection.CreateCommand();
    count.CommandText = $"SELECT count() FROM inventory_requests WHERE item_id = {id}";
    var result = await count.ExecuteScalarAsync(cancellationToken);

    return Convert.ToInt32(result);
}

static async Task<string> RecordAndReadRedis(IConnectionMultiplexer redis, int id, int available)
{
    var db = redis.GetDatabase();
    var key = $"inventory-events:{id}";
    var value = available.ToString();

    await db.ListLeftPushAsync(key, value);
    var result = await db.ListRightPopAsync(key);

    return result.ToString();
}

static Task PublishInventoryEvent(
    IPublishEndpoint publishEndpoint,
    int id,
    int available,
    CancellationToken cancellationToken)
{
    return publishEndpoint.Publish(new InventoryEventMessage(id, available, DateTimeOffset.UtcNow), cancellationToken);
}

internal sealed class InventoryEventConsumer(ILogger<InventoryEventConsumer> logger) : IConsumer<InventoryEventMessage>
{
    public Task Consume(ConsumeContext<InventoryEventMessage> context)
    {
        logger.LogInformation(
            "Consumed inventory event payload: {ItemId}/{Available} messageId={MessageId}",
            context.Message.ItemId,
            context.Message.Available,
            context.MessageId);
        return Task.CompletedTask;
    }
}

internal sealed class ClickHouseOptions(string connectionString)
{
    public string ConnectionString { get; } = connectionString;
}

internal sealed class ClickHouseInitializationState
{
    public SemaphoreSlim Lock { get; } = new(1, 1);
    public bool Initialized { get; set; }
}

internal sealed class RabbitMqOptions
{
    public string Host { get; init; } = "rabbitmq";
    public ushort Port { get; init; } = 5672;
    public string Username { get; init; } = "guest";
    public string Password { get; init; } = "guest";
    public string Queue { get; init; } = "sit.user-activity";
}

namespace OTelSample.ZeroCodeMassTransit.Messages
{
    public sealed record InventoryEvent(int ItemId, int Available, DateTimeOffset ObservedAt);
}
