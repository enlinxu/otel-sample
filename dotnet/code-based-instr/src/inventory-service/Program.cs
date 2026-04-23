using System.Diagnostics;
using System.Text;
using System.Text.Json;
using ClickHouse.Driver.ADO;
using Microsoft.Extensions.Options;
using Npgsql;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

var builder = WebApplication.CreateBuilder(args);

var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]
    ?? "http://opentelemetry-collector.default.svc.cluster.local:4317";
var postgresConnectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? "Host=postgres;Port=5432;Database=otel;Username=otel;Password=otel";
var clickHouseConnectionString = builder.Configuration.GetConnectionString("ClickHouse")
    ?? "Host=clickhouse;Protocol=http;Port=8123;Username=default;Password=";

builder.Services.AddSingleton(new NpgsqlDataSourceBuilder(postgresConnectionString).Build());
builder.Services.AddSingleton(new ClickHouseOptions(clickHouseConnectionString));
builder.Services.Configure<RabbitMqOptions>(builder.Configuration.GetSection("RabbitMq"));
builder.Services.AddSingleton(static serviceProvider =>
{
    var options = serviceProvider.GetRequiredService<IOptions<RabbitMqOptions>>().Value;

    return new ConnectionFactory
    {
        HostName = options.Host,
        Port = options.Port,
        UserName = options.Username,
        Password = options.Password
    };
});
builder.Services.AddHostedService<InventoryEventConsumer>();

builder.Services
    .AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("inventory-service"))
    .WithTracing(tracing =>
    {
        tracing
            .AddAspNetCoreInstrumentation()
            .AddSource("Npgsql")
            .AddSource(InventoryTelemetry.Source.Name)
            .AddOtlpExporter(options =>
            {
                options.Endpoint = new Uri(otlpEndpoint);
                options.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;
            });
    });

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var dataSource = scope.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
    var clickHouseOptions = scope.ServiceProvider.GetRequiredService<ClickHouseOptions>();
    await WaitForDependencies(dataSource, clickHouseOptions.ConnectionString, app.Logger);
}

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "inventory-service" }));

app.MapGet("/inventory/{id:int}", async (
    int id,
    NpgsqlDataSource dataSource,
    ClickHouseOptions clickHouseOptions,
    ConnectionFactory rabbitMqFactory,
    IOptions<RabbitMqOptions> rabbitMqOptions,
    CancellationToken cancellationToken) =>
{
    var available = await GetInventoryLevel(dataSource, id, cancellationToken);
    var clickHouseCount = await RecordAndCountClickHouse(clickHouseOptions.ConnectionString, id, available, cancellationToken);
    await PublishInventoryEvent(rabbitMqFactory, rabbitMqOptions.Value, id, available, cancellationToken);

    return Results.Ok(new
    {
        itemId = id,
        available,
        clickHouseCount,
        rabbitPublished = true
    });
});

app.Run();

static async Task WaitForDependencies(NpgsqlDataSource dataSource, string clickHouseConnectionString, ILogger logger)
{
    await Retry("PostgreSQL initialization", logger, () => InitializePostgres(dataSource));
    await Retry("ClickHouse initialization", logger, () => InitializeClickHouse(clickHouseConnectionString));
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

    using (var insertActivity = InventoryTelemetry.Source.StartActivity("clickhouse insert inventory-request", ActivityKind.Client))
    {
        insertActivity?.SetTag("db.system", "clickhouse");
        insertActivity?.SetTag("db.operation.name", "INSERT");
        insertActivity?.SetTag("db.namespace", "default");

        await using var insert = connection.CreateCommand();
        insert.CommandText = $"INSERT INTO inventory_requests (ts, item_id, available, source) VALUES (now(), {id}, {available}, 'inventory-http')";
        await insert.ExecuteNonQueryAsync(cancellationToken);
    }

    using var selectActivity = InventoryTelemetry.Source.StartActivity("clickhouse select inventory-request-count", ActivityKind.Client);
    selectActivity?.SetTag("db.system", "clickhouse");
    selectActivity?.SetTag("db.operation.name", "SELECT");
    selectActivity?.SetTag("db.namespace", "default");

    await using var count = connection.CreateCommand();
    count.CommandText = $"SELECT count() FROM inventory_requests WHERE item_id = {id}";
    var result = await count.ExecuteScalarAsync(cancellationToken);

    return Convert.ToInt32(result);
}

static async Task PublishInventoryEvent(
    ConnectionFactory rabbitMqFactory,
    RabbitMqOptions rabbitMqOptions,
    int id,
    int available,
    CancellationToken cancellationToken)
{
    using var publishActivity = InventoryTelemetry.Source.StartActivity("rabbitmq publish inventory-event", ActivityKind.Producer);
    publishActivity?.SetTag("messaging.system", "rabbitmq");
    publishActivity?.SetTag("messaging.destination.name", rabbitMqOptions.Queue);
    publishActivity?.SetTag("messaging.operation.type", "publish");

    await using var connection = await rabbitMqFactory.CreateConnectionAsync(cancellationToken);
    await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

    await channel.QueueDeclareAsync(
        queue: rabbitMqOptions.Queue,
        durable: true,
        exclusive: false,
        autoDelete: false,
        arguments: null,
        cancellationToken: cancellationToken);

    var payload = JsonSerializer.SerializeToUtf8Bytes(new InventoryEvent(id, available, DateTimeOffset.UtcNow));
    var properties = new BasicProperties
    {
        ContentType = "application/json"
    };

    await channel.BasicPublishAsync(
        exchange: string.Empty,
        routingKey: rabbitMqOptions.Queue,
        mandatory: false,
        basicProperties: properties,
        body: payload,
        cancellationToken: cancellationToken);
}

internal sealed class InventoryEventConsumer(
    ConnectionFactory rabbitMqFactory,
    IOptions<RabbitMqOptions> rabbitMqOptions,
    ILogger<InventoryEventConsumer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = rabbitMqOptions.Value;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var connection = await rabbitMqFactory.CreateConnectionAsync(stoppingToken);
                await using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);

                await channel.QueueDeclareAsync(
                    queue: options.Queue,
                    durable: true,
                    exclusive: false,
                    autoDelete: false,
                    arguments: null,
                    cancellationToken: stoppingToken);

                var consumer = new AsyncEventingBasicConsumer(channel);
                consumer.ReceivedAsync += async (_, delivery) =>
                {
                    using var consumeActivity = InventoryTelemetry.Source.StartActivity("rabbitmq consume inventory-event", ActivityKind.Consumer);
                    consumeActivity?.SetTag("messaging.system", "rabbitmq");
                    consumeActivity?.SetTag("messaging.destination.name", options.Queue);
                    consumeActivity?.SetTag("messaging.operation.type", "receive");

                    logger.LogInformation(
                        "Consumed inventory event payload: {Payload}",
                        Encoding.UTF8.GetString(delivery.Body.ToArray()));
                    await Task.CompletedTask;
                };

                await channel.BasicConsumeAsync(
                    queue: options.Queue,
                    autoAck: true,
                    consumer: consumer,
                    cancellationToken: stoppingToken);

                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "RabbitMQ consumer setup failed; retrying in 2s");
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }
    }
}

internal sealed record InventoryEvent(int ItemId, int Available, DateTimeOffset ObservedAt);

internal sealed class ClickHouseOptions(string connectionString)
{
    public string ConnectionString { get; } = connectionString;
}

internal sealed class RabbitMqOptions
{
    public string Host { get; init; } = "rabbitmq";
    public int Port { get; init; } = 5672;
    public string Username { get; init; } = "guest";
    public string Password { get; init; } = "guest";
    public string Queue { get; init; } = "inventory-events";
}

internal static class InventoryTelemetry
{
    public static readonly ActivitySource Source = new("otel-sample.inventory");
}
