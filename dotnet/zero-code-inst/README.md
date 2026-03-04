# .NET OpenTelemetry sample: zero-code auto-instrumentation

This sample keeps app code free of OpenTelemetry SDK setup.
Instrumentation is injected at runtime in Kubernetes using the .NET auto-instrumentation profiler/startup hooks.

Trace types targeted:
- server spans (ASP.NET Core)
- client spans (`HttpClient`)
- database spans (`Npgsql`/ADO.NET activity capture)

## What enables traces (the secret)

In zero-code mode, app code does not enable tracing.
Tracing is enabled by Kubernetes runtime injection in `k8s/otel-sample.yaml`.

The required switches are:

1. Init container copies the auto-instrumentation binaries:

```yaml
initContainers:
- name: copy-auto-instrumentation
  image: alpine:3.20
  command:
  - sh
  - -c
  - apk add --no-cache curl unzip && curl -sSfLo /tmp/otel-dotnet-auto-install.sh https://github.com/open-telemetry/opentelemetry-dotnet-instrumentation/releases/latest/download/otel-dotnet-auto-install.sh && OTEL_DOTNET_AUTO_HOME=/otel-auto VERSION=v1.13.0 sh /tmp/otel-dotnet-auto-install.sh
```

2. Profiler/startup hook env vars attach instrumentation to the .NET process:

```yaml
- name: CORECLR_ENABLE_PROFILING
  value: "1"
- name: CORECLR_PROFILER
  value: "{918728DD-259F-4A6A-AC2B-B85E1B658318}"
- name: CORECLR_PROFILER_PATH
  value: /otel-auto/linux-arm64/OpenTelemetry.AutoInstrumentation.Native.so
- name: DOTNET_STARTUP_HOOKS
  value: /otel-auto/net/OpenTelemetry.AutoInstrumentation.StartupHook.dll
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
  value: order-service      # in order-service deployment
- name: OTEL_SERVICE_NAME
  value: inventory-service  # in inventory-service deployment
```

If `OTEL_SERVICE_NAME` does not align with the service/deployment naming convention used by your topology tool, dependency edges can be missing even when spans exist.

If these init/env settings are missing, zero-code tracing will not start.

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

## Verify

Tempo API search:

```bash
kubectl -n monitoring get --raw '/api/v1/namespaces/monitoring/services/tempo:3200/proxy/api/search?tags=service.name=order-service&limit=5'
```

## Notes

- Auto-instrumentation is configured in `k8s/otel-sample.yaml` with:
  - initContainer runs official installer script (`otel-dotnet-auto-install.sh`) with `VERSION=v1.13.0`
  - profiler env vars (`CORECLR_*`, `DOTNET_*`, `OTEL_*`)
- If your runtime/architecture differs, adjust `CORECLR_PROFILER_PATH`.
