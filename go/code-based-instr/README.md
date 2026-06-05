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

For PostgreSQL, `otelpgx` is attached to the pgxpool config — no manual spans needed:

```go
config.ConnConfig.Tracer = otelpgx.NewTracer()
pool, _ := pgxpool.NewWithConfig(ctx, config)
```

For Redis, `redisotel` adds an instrumentation hook to the client — no manual spans needed:

```go
redisotel.InstrumentTracing(redisClient)
```

For ClickHouse, `otelsql` wraps the `clickhouse-go/v2` driver — no manual spans needed:

```go
driverName, _ := otelsql.Register("clickhouse",
    otelsql.AllowRoot(),
    otelsql.TraceQueryWithoutArgs(),
    otelsql.WithDatabaseName("default"),
    otelsql.WithSystem(attribute.String("db.system", "clickhouse")),
)
db, _ := sql.Open(driverName, "clickhouse://user:pass@host:9000/default")
```

For RabbitMQ, there is no native OTel library for `amqp091-go`, so spans are created manually:

```go
ctx, span := tracer.Start(ctx, "messaging.rabbitmq.publish inventory-events",
    trace.WithSpanKind(trace.SpanKindProducer),
    trace.WithAttributes(...),
)
defer span.End()
```

## Prompt For Agent

```text
Read go/code-based-instr in this repo and use it as the reference implementation for instrumenting my Go application.

Goal:
Add OpenTelemetry traces for RPC traffic, database traffic, cache traffic, and messaging traffic in my Go codebase.

How this sample instruments each dependency:
- PostgreSQL: otelpgx tracer attached to pgxpool — spans are automatic, no manual tracer.Start() needed
- Redis: redisotel hook on the go-redis client — spans are automatic, no manual tracer.Start() needed
- ClickHouse: otelsql wrapping the clickhouse-go/v2 database/sql driver — spans are automatic
- RabbitMQ: manual tracer.Start() spans — no native OTel library exists for amqp091-go

IMPORTANT — match the library in use, not the library in the sample:
The sample uses pgx, go-redis, and clickhouse-go/v2. Your codebase may use different libraries
(e.g. gorm, sqlx, database/sql with a different driver, pgconn, another redis client). Do not
replace the existing database or cache library. Instead:
1. Inspect the codebase to identify which library is actually used for each dependency.
2. Find the OTel instrumentation package that wraps that specific library
   (examples: otelsql for any database/sql driver, otelgorm for gorm, uptrace/bun instrumentation for bun).
3. Wire in that instrumentation package using the same pattern this sample uses for its libraries.
4. Fall back to manual tracer.Start() spans only if no native OTel library exists for the library in use.

What to copy from the example regardless of library choice:
- OpenTelemetry tracer provider setup (internal/telemetry/telemetry.go)
- RPC server and client instrumentation pattern using a stats handler or interceptor
- OTLP exporter wiring and service naming
- Standard OpenTelemetry semantic convention attributes for any manual spans (db.system, messaging.system, etc.)

What to do in my codebase:
- Identify my RPC boundary and instrument it the way this sample instruments gRPC
- For each dependency, find and wire the native OTel library for the library already in use
- Add manual tracer.Start() spans only where no native library is available
- Preserve business logic and only add the minimum tracing/config changes needed
- Explain any gaps where your stack has no native OTel library

Deliverables:
- code changes
- config/env changes
- short mapping: my dependency → library in use → OTel instrumentation package chosen
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
