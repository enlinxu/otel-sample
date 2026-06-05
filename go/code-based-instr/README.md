# Go OpenTelemetry sample: code-based instrumentation

This sample instruments Go services in application code with the OpenTelemetry Go SDK.

Trace types shown:
- RPC server spans (`order-service`, `inventory-service` via gRPC + `otelgrpc`)
- RPC client spans (`order-service` -> `inventory-service` via gRPC client)
- database spans (`inventory-service` -> PostgreSQL via explicit spans)
- database spans for ClickHouse via explicit custom spans
- cache spans for Redis via explicit custom spans
- messaging spans for RabbitMQ via explicit custom spans

This sample request path does all of the following inside `inventory-service`:

- PostgreSQL read
- ClickHouse insert + select
- Redis set + get
- RabbitMQ publish + consume

## What enables traces (the secret)

These pieces are the switch that turns tracing on:

```go
tp, _ := telemetry.NewTracerProvider(ctx, "inventory-service")
otel.SetTracerProvider(tp)
```

For RPC spans, these gRPC hooks are what make inbound and outbound traffic show up:

```go
grpc.NewServer(grpc.StatsHandler(otelgrpc.NewServerHandler()))
grpc.NewClient(addr, grpc.WithStatsHandler(otelgrpc.NewClientHandler()))
```

For the manual spans around PostgreSQL, ClickHouse, Redis, and RabbitMQ, this is the critical pattern:

```go
ctx, span := tracer.Start(ctx, "messaging.rabbitmq.publish inventory-events",
    trace.WithSpanKind(trace.SpanKindProducer),
    trace.WithAttributes(...semantic convention attributes...),
)
defer span.End()
```

If those hooks and `tracer.Start(...)` calls are missing, you will not get the app-level traces this sample is meant to demonstrate.

## Prompt For Agent

```text
Read go/code-based-instr in this repo and use it as the reference implementation for instrumenting my Go application.

Goal:
Add OpenTelemetry traces for RPC traffic, database traffic, cache traffic, and messaging traffic in my Go codebase.

What to copy from the example:
- OpenTelemetry tracer provider setup
- gRPC server and client instrumentation using otelgrpc
- explicit dependency spans for PostgreSQL, ClickHouse, Redis, and RabbitMQ
- OTLP exporter wiring and service naming
- use of standard OpenTelemetry semantic convention attributes for RPC, DB, and messaging spans

What to do in my codebase:
- Identify my RPC boundary and instrument it the way this sample instruments gRPC
- Add explicit spans around dependencies that need code-level instrumentation
- Preserve my business logic and only add the minimum tracing/config changes needed
- Reuse semantic conventions from this sample rather than inventing custom keys for standard dependencies
- Explain any gaps where my stack differs from this sample

Deliverables:
- code changes
- config/env changes
- short mapping from my dependencies to the corresponding example files in this repo
- expected trace categories after the change
```

## Why this follows semantic conventions

- RPC spans come from `otelgrpc`, so standard gRPC/RPC attributes are emitted automatically.
- Resource attributes use OpenTelemetry semantic conventions:
  - `service.name`
  - `service.version`
  - host/process/sdk resource fields
- Dependency spans use standard semantic keys such as:
  - `db.system`
  - `db.name`
  - `db.operation.name`
  - `messaging.system`
  - `messaging.destination.name`
  - `messaging.operation.name`

## Deploy to kind

1. Build and load images:

```bash
cd go/code-based-instr
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
kubectl -n otel-sample-go rollout status deploy/postgres
kubectl -n otel-sample-go rollout status deploy/redis
kubectl -n otel-sample-go rollout status deploy/rabbitmq
kubectl -n otel-sample-go rollout status deploy/clickhouse
kubectl -n otel-sample-go rollout status deploy/inventory-service
kubectl -n otel-sample-go rollout status deploy/order-service
```

4. Generate RPC traffic:

```bash
kubectl -n otel-sample-go port-forward svc/order-service 39090:9090
```

In another terminal:

```bash
grpcurl -plaintext -d '{"item_id":1}' localhost:39090 otelsample.v1.OrderService/GetOrder
```

## Verify

Tempo API search:

```bash
kubectl -n monitoring get --raw '/api/v1/namespaces/monitoring/services/tempo:3200/proxy/api/search?tags=service.name=order-service&limit=5'
```

## Files to review

- `proto/otel_sample.proto`
- `cmd/order-service/main.go`
- `cmd/inventory-service/main.go`
- `internal/telemetry/telemetry.go`
- `k8s/otel-sample.yaml`
