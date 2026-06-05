# .NET OpenTelemetry sample: zero-code auto-instrumentation with MassTransit

This sample is designed to reproduce the customer's likely messaging stack more closely:

- zero-code .NET auto-instrumentation
- `MassTransit` over `RabbitMQ`
- swappable auto-instrumentation version via `OTEL_AUTO_VERSION`

The request path is still:

- inbound HTTP to `order-service`
- outbound HTTP to `inventory-service`
- PostgreSQL read
- ClickHouse insert + select
- Redis write + read
- MassTransit publish + consume over RabbitMQ

## Why this sample exists

`dotnet/zero-code-inst` uses raw `RabbitMQ.Client` and emits RabbitMQ-native messaging spans.

This sample uses `MassTransit`, which is closer to the customer trace shape. In particular, it is intended to help reproduce spans such as:

- `sit.user-activity send`
- `sit.user-activity receive`
- `sit.user-activity process`
- `messaging.masstransit.*` attributes

## What enables traces

Tracing is enabled by runtime injection in [k8s/otel-sample.yaml](/Users/enlinxu/Projects/go-workspace/src/github.com/enlinxu/otel-sample/dotnet/zero-code-masstransit/k8s/otel-sample.yaml).

The critical switch for version testing is:

```yaml
- name: OTEL_AUTO_VERSION
  value: v1.9.0
```

That value is used by the init container to download the matching .NET auto-instrumentation release.

To test whether upgrading changes the trace shape, change it to:

```yaml
- name: OTEL_AUTO_VERSION
  value: v1.13.0
```

## Prompt For Agent

```text
Read dotnet/zero-code-masstransit in this repo and use it as the reference implementation when my .NET application uses MassTransit over RabbitMQ.

Goal:
Enable zero-code tracing in my deployment and set correct expectations for MassTransit messaging spans.

What to copy from the example:
- runtime injection pattern for .NET auto-instrumentation
- OTEL_AUTO_VERSION version pinning and swap testing
- service naming and exporter env vars
- the expectation that MassTransit consumer spans may differ from raw RabbitMQ spans

What to do in my codebase/deployment:
- Keep app code unchanged unless there is a hard gap
- Adjust deployment/runtime settings to match the zero-code pattern in this repo
- Verify whether my traces contain MassTransit-specific attributes such as messaging.masstransit.*
- Do not assume consumer spans will look like raw RabbitMQ.Client spans

If my downstream tooling expects RabbitMQ-native consumer span shape:
- call that out explicitly
- compare the traces against this sample
- recommend parser normalization or code-based instrumentation only if needed

Deliverables:
- deployment/runtime changes
- version recommendation for OTEL_AUTO_VERSION
- short explanation of expected MassTransit span shape
- any incompatibilities with tooling that assumes raw RabbitMQ spans
```

## Deploy to kind

```bash
cd dotnet/zero-code-masstransit
./build-and-load-kind.sh
kubectl apply -f k8s/otel-sample.yaml
kubectl -n otel-sample-zero-masstransit rollout status deploy/postgres
kubectl -n otel-sample-zero-masstransit rollout status deploy/rabbitmq
kubectl -n otel-sample-zero-masstransit rollout status deploy/redis
kubectl -n otel-sample-zero-masstransit rollout status deploy/inventory-service
kubectl -n otel-sample-zero-masstransit rollout status deploy/order-service
```

Generate traffic:

```bash
kubectl -n otel-sample-zero-masstransit port-forward svc/order-service 38080:8080
for i in {1..20}; do curl -s http://localhost:38080/order/1 > /dev/null; done
```

## What to compare

Compare the messaging consumer spans from this sample against the customer's trace:

- whether consumer spans have `messaging.system=rabbitmq`
- whether the consumer operation is `receive`, `process`, or `deliver`
- whether the consumer spans use `messaging.masstransit.*`
- whether parent-child linkage differs from the raw `RabbitMQ.Client` sample

## Expected outcome

If the customer issue is caused by `MassTransit` span shape rather than pure version drift, this sample should reproduce that difference more accurately than `dotnet/zero-code-inst`.
