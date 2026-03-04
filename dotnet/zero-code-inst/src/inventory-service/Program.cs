using Npgsql;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? "Host=postgres;Port=5432;Database=otel;Username=otel;Password=otel";

builder.Services.AddSingleton(new NpgsqlDataSourceBuilder(connectionString).Build());

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var dataSource = scope.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
    await InitializeDatabase(dataSource);
}

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "inventory-service" }));

app.MapGet("/inventory/{id:int}", async (int id, NpgsqlDataSource dataSource, CancellationToken cancellationToken) =>
{
    await using var command = dataSource.CreateCommand("SELECT available FROM inventory WHERE item_id = @id");
    command.Parameters.AddWithValue("id", id);

    var result = await command.ExecuteScalarAsync(cancellationToken);
    var available = result is DBNull or null ? 0 : Convert.ToInt32(result);

    return Results.Ok(new
    {
        itemId = id,
        available
    });
});

app.Run();

static async Task InitializeDatabase(NpgsqlDataSource dataSource)
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
