# Java OpenTelemetry Sample: Auto-Instrumentation with Java Agent

This sample demonstrates zero-code auto-instrumentation for Java 17 applications using the OpenTelemetry Java Agent (v2.6.1).

## Architecture

```
┌──────────────┐    ┌──────────────┐    ┌──────────────────┐    ┌─────────────────┐
│   Client     │───▶│ order-svc    │───▶│   HAProxy        │───▶│ inventory-svc   │
│   (curl)     │    │   (Java 17)  │    │ (external LB)    │    │    (Java 17)    │
└──────────────┘    └──────────────┘    └──────────────────┘    └────────┬────────┘
                                                                         │
                        ┌───────────────────────────────────────────────┤
                        │                                               │
                   ┌────▼─────┐                                    ┌────▼────┐
                   │ Postgres │                                    │RabbitMQ │
                   └──────────┘                                    └────────┘
```

This topology mirrors your production setup where:
- Java services run inside Kubernetes
- HAProxy runs outside K8s (simulated here as an in-cluster service)

## What the Java Agent Instruments

| Component | Instrumentation | How |
|-----------|----------------|-----|
| HTTP Server | `server` span | Auto from Spring Web/Undertow |
| HTTP Client | `client` span | Auto from Apache HttpClient/OkHttp |
| Database | `db` span | Auto via JDBC instrumentation |
| RabbitMQ | `messaging` span | Auto via RabbitMQ client instrumentation |

## Key Configuration

The Java agent is enabled via `JAVA_TOOL_OPTIONS`:

```yaml
- name: JAVA_TOOL_OPTIONS
  value: "-javaagent:/app/opentelemetry-javaagent.jar"
```

Environment variables for the agent:

```yaml
- name: OTEL_SERVICE_NAME
  value: order-service
- name: OTEL_EXPORTER_OTLP_ENDPOINT
  value: http://opentelemetry-collector.default.svc.cluster.local:4317
- name: OTEL_JAVAAGENT_ENABLED
  value: "true"
```

## Prompt For Agent

```text
Read java/auto-instr in this repo and use it as the reference implementation for zero-code tracing in my Java application.

What to copy from the example:
- JAVA_TOOL_OPTIONS javaagent injection
- OTEL_SERVICE_NAME and OTLP exporter environment variables
- deployment-level configuration rather than application code changes
- propagation expectations across HAProxy or similar load balancers

What to do in my codebase/deployment:
- Add the Java agent and runtime env vars to my deployment manifests or startup scripts
- Keep application code unchanged unless there is a proven gap
- Verify inbound HTTP, outbound HTTP, database, and RabbitMQ spans if my app uses those components
- Preserve trace headers across proxies and load balancers
- Explain any dependency gaps that the agent may not cover well enough

Deliverables:
- deployment/startup changes
- exact env vars and javaagent flag added
- expected trace types
- any known propagation or dependency caveats
```

## Deploy to Kind

1. Build and load images:

```bash
cd java/auto-instr
./build-and-load-kind.sh
```

2. Build HAProxy image:

```bash
cd haproxy
docker build -t otel-java/haproxy:latest .
kind load docker-image otel-java/haproxy:latest
```

3. Deploy Tempo if needed:

```bash
kubectl -n monitoring get svc tempo || kubectl apply -f k8s/tempo.yaml
kubectl -n monitoring rollout status deploy/tempo
```

4. Deploy sample:

```bash
kubectl apply -f k8s/otel-sample.yaml
kubectl apply -f k8s/haproxy.yaml
kubectl -n otel-java-sample rollout status deploy/postgres
kubectl -n otel-java-sample rollout status deploy/inventory-service
kubectl -n otel-java-sample rollout status deploy/order-service
kubectl -n otel-java-sample rollout status deploy/haproxy
```

5. Generate traffic:

```bash
kubectl -n otel-java-sample port-forward svc/order-service 18080:8080
```

In another terminal:

```bash
for i in {1..20}; do curl -s http://localhost:18080/order/1 > /dev/null; done
```

## Verify Traces

Tempo API search:

```bash
kubectl -n monitoring get --raw '/api/v1/namespaces/monitoring/services/tempo:3200/proxy/api/search?tags=service.name=order-service&limit=5'
```

Query for specific span types:

```bash
# HTTP spans
kubectl -n monitoring get --raw '/api/v1/namespaces/monitoring/services/tempo:3200/proxy/api/search/tag/http.route/values'

# Database spans
kubectl -n monitoring get --raw '/api/v1/namespaces/monitoring/services/tempo:3200/proxy/api/search/tag/db.system/values'

# Messaging spans
kubectl -n monitoring get --raw '/api/v1/namespaces/monitoring/services/tempo:3200/proxy/api/search/tag/messaging.system/values'
```

## Expected Spans

A single `/order/1` request should produce:

1. `order-service` server span (inbound)
2. `order-service` HTTP client span → HAProxy
3. HAProxy backend span (if using TCP mode, may not appear)
4. `inventory-service` server span (inbound)
5. `inventory-service` database client span → PostgreSQL
6. `inventory-service` messaging span → RabbitMQ

## HAProxy Setup Notes

In this sample, HAProxy acts as a passthrough load balancer. For trace continuity:

1. **W3C Trace Context**: HAProxy passes `traceparent` header by default
2. **Service Naming**: Ensure `OTEL_SERVICE_NAME` matches your topology tool's expectations
3. **If traces break at HAProxy**: Check if HAProxy strips headers or use TCP mode

## Common Issues

### Traces don't span across HAProxy
- HAProxy may be dropping `traceparent` headers
- Solution: Use `option forwardfor` and configure HAProxy to preserve headers

### Service name doesn't appear in topology
- Verify `OTEL_SERVICE_NAME` env var matches expected naming
- Check collector is receiving traces: `kubectl logs -n default deploy/collector`

### Java Agent not loading
- Verify `JAVA_TOOL_OPTIONS` contains `-javaagent:` path
- Check agent JAR exists in container at expected path
- Verify agent version compatibility with Java 17
