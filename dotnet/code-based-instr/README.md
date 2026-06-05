# .NET OpenTelemetry sample: code-based instrumentation

This sample instruments .NET services in application code (`Program.cs`) with the OpenTelemetry SDK.

Trace types shown:
- server spans (`order-service`, `inventory-service` via ASP.NET Core)
- client spans (`order-service` -> `inventory-service` via `HttpClient`)
- database spans (`inventory-service` -> PostgreSQL via `Npgsql`)
- database spans for ClickHouse via explicit custom spans
- messaging spans for RabbitMQ via explicit custom spans

This sample request path does all of the following inside `inventory-service`:

- PostgreSQL read
- ClickHouse insert + select
- RabbitMQ publish + consume

## What enables traces (the secret)

These lines in `Program.cs` are the switch that turns tracing on:

```csharp
builder.Services
    .AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddSource("Npgsql")
            .AddOtlpExporter(...);
    });
```

And this points traces to your collector:

```csharp
var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]
    ?? "http://opentelemetry-collector.default.svc.cluster.local:4317";
```

If these lines are missing, you will not get the app-level traces shown in this sample.

For the non-default dependencies in this sample, the important part is:

```csharp
.AddSource(InventoryTelemetry.Source.Name)
```

That enables the explicit spans created around:

- ClickHouse insert/select
- RabbitMQ publish/consume

Without that `ActivitySource`, PostgreSQL still works through `Npgsql`, but ClickHouse and RabbitMQ would not have the same explicit span shape this sample demonstrates.

## Prompt For Agent

```text
Read dotnet/code-based-instr in this repo and use it as the reference implementation for instrumenting my .NET application in code.

What to copy from the example:
- OpenTelemetry SDK setup in Program.cs
- AspNetCore server instrumentation
- HttpClient client instrumentation
- OTLP exporter wiring
- ActivitySource-based custom spans for dependencies that are not captured well enough automatically
- service.name and collector endpoint conventions used in this repo

What to do in my codebase:
- Add the minimum package references and Program.cs startup configuration needed
- Instrument inbound HTTP, outbound HTTP, database calls, and messaging calls if my app has them
- Reuse standard OpenTelemetry semantic conventions for dependency spans
- Preserve my current business logic and only add instrumentation/config changes
- If a dependency is not automatically captured with the quality shown in this sample, add explicit spans the way this example does for ClickHouse and RabbitMQ

Deliverables:
- code changes
- config/env changes
- short explanation of which trace types are now covered
- any remaining gaps compared with this sample
```

## Deploy to kind

1. Build and load images:

```bash
cd dotnet/code-based-instr
./build-and-load-kind.sh
```

2. Deploy Tempo if needed:

```bash
kubectl -n monitoring get svc tempo || kubectl apply -f k8s/tempo.yaml
kubectl -n monitoring rollout status deploy/tempo
```

3. Deploy sample:

```bash
kubectl apply -f k8s/otel-sample.yaml
kubectl -n otel-sample rollout status deploy/postgres
kubectl -n otel-sample rollout status deploy/inventory-service
kubectl -n otel-sample rollout status deploy/order-service
```

4. Generate traffic:

```bash
kubectl -n otel-sample port-forward svc/order-service 18080:8080
```

In another terminal:

```bash
for i in {1..20}; do curl -s http://localhost:18080/order/1 > /dev/null; done
```

## Verify

Collector metrics:

```bash
kubectl -n default get --raw '/api/v1/namespaces/default/services/opentelemetry-collector:8888/proxy/metrics' \
  | grep 'otelcol_receiver_accepted_spans_total'
```

Tempo API search:

```bash
kubectl -n monitoring get --raw '/api/v1/namespaces/monitoring/services/tempo:3200/proxy/api/search?tags=service.name=order-service&limit=5'
```
