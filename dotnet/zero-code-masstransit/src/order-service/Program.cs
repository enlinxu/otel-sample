var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpClient("inventory", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["INVENTORY_SERVICE_URL"] ?? "http://inventory-service:8080");
});

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "order-service" }));

app.MapGet("/order/{id:int}", async (int id, IHttpClientFactory httpClientFactory, CancellationToken cancellationToken) =>
{
    var client = httpClientFactory.CreateClient("inventory");
    var response = await client.GetFromJsonAsync<InventoryResponse>($"/inventory/{id}", cancellationToken);

    if (response is null)
    {
        return Results.Problem("Inventory response was empty", statusCode: 502);
    }

    return Results.Ok(new
    {
        orderId = id,
        status = "processed",
        inventory = response
    });
});

app.Run();

internal sealed record InventoryResponse(int ItemId, int Available);
