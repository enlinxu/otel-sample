using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]
    ?? "http://opentelemetry-collector.default.svc.cluster.local:4317";

builder.Services.AddHttpClient("inventory", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["INVENTORY_SERVICE_URL"] ?? "http://inventory-service:8080");
});

builder.Services
    .AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("order-service"))
    .WithTracing(tracing =>
    {
        tracing
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddOtlpExporter(options =>
            {
                options.Endpoint = new Uri(otlpEndpoint);
                options.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;
            });
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
