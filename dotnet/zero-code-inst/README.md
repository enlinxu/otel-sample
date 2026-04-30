# .NET OpenTelemetry sample: zero-code auto-instrumentation

This sample keeps app code free of OpenTelemetry SDK setup.
Instrumentation is injected at runtime in Kubernetes using the .NET auto-instrumentation profiler/startup hooks.

Trace types targeted:
- server spans (ASP.NET Core)
- client spans (`HttpClient`)
- database spans (`Npgsql`/ADO.NET activity capture)
- messaging spans (`RabbitMQ.Client`)

This sample request path does all of the following inside `inventory-service`:

- PostgreSQL read
- ClickHouse insert + select
- Redis write + read
- RabbitMQ publish + consume

## What enables traces (the secret)

In zero-code mode, app code does not enable tracing.
Tracing is enabled by Kubernetes runtime injection in `k8s/otel-sample.yaml`.

The required switches are:

1. Init container copies the auto-instrumentation binaries. The version is swappable with `OTEL_AUTO_VERSION`:

```yaml
initContainers:
- name: copy-auto-instrumentation
  image: alpine:3.20
  env:
  - name: OTEL_AUTO_VERSION
    value: v1.13.0
```

2. Profiler/startup hook env vars attach instrumentation to the .NET process:

```yaml
- name: CORECLR_ENABLE_PROFILING
  value: "1"
- name: CORECLR_PROFILER
  value: "{918728DD-259F-4A6A-AC2B-B85E1B658318}"
- name: CORECLR_PROFILER_PATH
  value: /otel-auto/auto/linux-arm64/OpenTelemetry.AutoInstrumentation.Native.so
- name: DOTNET_STARTUP_HOOKS
  value: /otel-auto/auto/net/OpenTelemetry.AutoInstrumentation.StartupHook.dll
```

3. Exporter env vars send traces to your collector:

```yaml
- name: OTEL_TRACES_EXPORTER
  value: otlp
- name: OTEL_EXPORTER_OTLP_ENDPOINT
  value: http://opentelemetry-collector.default.svc.cluster.local:4317
- name: OTEL_EXPORTER_OTLP_PROTOCOL
  value: grpc
```

4. Keep service naming aligned for topology tools:

```yaml
- name: OTEL_SERVICE_NAME
  value: order-service
- name: OTEL_SERVICE_NAME
  value: inventory-service
```

If `OTEL_SERVICE_NAME` does not align with the service/deployment naming convention used by your topology tool, dependency edges can be missing even when spans exist.

## Deploy to kind

1. Build and load images:

```bash
cd dotnet/zero-code-inst
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
kubectl -n otel-sample-zero rollout status deploy/postgres
kubectl -n otel-sample-zero rollout status deploy/inventory-service
kubectl -n otel-sample-zero rollout status deploy/order-service
```

4. Generate traffic:

```bash
kubectl -n otel-sample-zero port-forward svc/order-service 28080:8080
```

In another terminal:

```bash
for i in {1..20}; do curl -s http://localhost:28080/order/1 > /dev/null; done
```

## What we observed with zero-code

Deployed and verified on a local kind cluster:

- `GET /order/{id:int}` server spans: present
- `order-service -> inventory-service` HTTP client spans: present
- PostgreSQL spans: present with `db.system=postgresql`
- Redis spans: present with `db.system=redis`
- RabbitMQ spans: present with `messaging.system=rabbitmq`
- ClickHouse traffic: present only as outbound HTTP spans to the ClickHouse service

Important limitation:

- Zero-code did not produce first-class ClickHouse DB spans in this sample.
- In Tempo, `db.system` values included `postgresql` and `redis`, but not `clickhouse`.
- The ClickHouse calls were visible as `System.Net.Http` client spans because this driver talks over HTTP.

Practical takeaway:

- Zero-code is enough for HTTP, PostgreSQL, Redis, and raw RabbitMQ client spans in this sample.
- Zero-code is not enough if you need ClickHouse to appear as a real database dependency with DB semantics.
- For ClickHouse, use the code-based sample and add explicit spans around the ClickHouse calls.
