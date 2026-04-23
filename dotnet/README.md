# .NET OpenTelemetry samples

This directory contains two implementations for the same demo app:

- `code-based-instr`: OpenTelemetry SDK configured in app code.
- `zero-code-inst`: no OTel SDK code; instrumentation injected at runtime using .NET auto-instrumentation.

## Read first

- [Code-based instrumentation](./code-based-instr/README.md)
- [Zero-code auto-instrumentation](./zero-code-inst/README.md)

## Difference and tradeoffs

| Dimension | Code-based instrumentation | Zero-code auto-instrumentation |
|---|---|---|
| App code changes | Required (`Program.cs` + packages) | Not required |
| Time to first trace | Slower (code + build + release) | Faster (deployment/env changes) |
| Control/precision | Highest | Medium |
| Custom spans/attributes | Easy (full control in code) | Limited (mainly config-based) |
| Stability over time | High, explicit in code | Can drift with runtime/agent/image versions |
| Runtime/version constraints | Lower | Higher (profiler/startup hook compatibility) |
| Operational complexity | Lower day-2 | Higher day-2 (injection/env/debugging) |
| Best for | Long-term production standard | Fast bootstrap for legacy estates |

## Practical implications

### Code-based (`code-based-instr`)

Pros:
- Explicit and reviewable in app code.
- Easier to tune span names/attributes and add business context.
- Usually easier to debug because behavior is app-version pinned.

Cons:
- Requires engineering time and service release cycles.
- Harder to roll out quickly across many existing services.

### Zero-code (`zero-code-inst`)

Pros:
- Fastest path when teams cannot modify code.
- Good for proving value and getting baseline service maps quickly.

Cons:
- More moving parts in Kubernetes manifests (init container, env vars, profiler paths).
- Compatibility constraints can cause partial instrumentation.
- Topology tools may need service-name alignment (for example `OTEL_SERVICE_NAME`) or edges can appear incomplete.

## Recommended adoption pattern

1. Start with `zero-code-inst` to get immediate baseline traces.
2. Move critical services to `code-based-instr` for durable, high-fidelity observability.
3. Keep zero-code for low-priority/legacy services until they are migrated.

## In this repo

- `code-based-instr` and `zero-code-inst` implement the same functional app path:
  `order-service` -> `inventory-service` -> `postgres` + `clickhouse` + `rabbitmq`
- The business request path is:
  - inbound HTTP to `order-service`
  - outbound HTTP from `order-service` to `inventory-service`
  - PostgreSQL read in `inventory-service`
  - ClickHouse write + query in `inventory-service`
  - RabbitMQ publish + consume in `inventory-service`

## Observed zero-code gap

After deploying the zero-code sample on kind and querying Tempo raw traces:

- HTTP server/client spans: present
- PostgreSQL spans: present as database spans (`db.system=postgresql`)
- RabbitMQ spans: present as messaging spans (`messaging.system=rabbitmq`)
- ClickHouse traffic: present only as generic HTTP client spans to the ClickHouse service

What is missing in zero-code:

- No first-class ClickHouse database spans
- No `db.system=clickhouse` in Tempo

Why this matters:

- From an RCA perspective, RabbitMQ is usable with zero-code in this sample.
- ClickHouse is only partially visible with zero-code because it looks like outbound HTTP, not database traffic.
- If you want ClickHouse to appear as a database dependency with DB-specific semantics, use `code-based-instr` and add explicit spans around the ClickHouse calls.
