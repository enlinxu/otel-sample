# .NET OpenTelemetry samples

This directory now contains three implementations of the demo app:

- `code-based-instr`: OpenTelemetry SDK configured in app code.
- `zero-code-inst`: zero-code auto-instrumentation with raw `RabbitMQ.Client`.
- `zero-code-masstransit`: zero-code auto-instrumentation with `MassTransit` over `RabbitMQ`.

## Read first

- [Code-based instrumentation](./code-based-instr/README.md)
- [Zero-code auto-instrumentation](./zero-code-inst/README.md)
- [Zero-code auto-instrumentation with MassTransit](./zero-code-masstransit/README.md)

## Why there are two zero-code samples

The two zero-code samples are intentionally different:

- `zero-code-inst` shows the trace shape for direct `RabbitMQ.Client` usage.
- `zero-code-masstransit` is meant to reproduce a customer environment that uses `MassTransit`, where messaging spans often look different.

That matters because a parser that works for raw RabbitMQ spans may still fail on MassTransit consumer spans.

## Difference and tradeoffs

| Dimension | Code-based instrumentation | Zero-code (`RabbitMQ.Client`) | Zero-code (`MassTransit`) |
|---|---|---|---|
| App code changes | Required | Not required | Not required for OTel, but app uses MassTransit |
| Time to first trace | Slower | Faster | Faster |
| Messaging span shape | Full control | RabbitMQ-native | MassTransit-shaped |
| Best use | Long-term production standard | Baseline zero-code reference | Customer reproduction for MassTransit stacks |
| Auto version testing | N/A | `OTEL_AUTO_VERSION` in manifest | `OTEL_AUTO_VERSION` in manifest |

## In this repo

All variants implement the same business flow:

- inbound HTTP to `order-service`
- outbound HTTP from `order-service` to `inventory-service`
- PostgreSQL read in `inventory-service`
- ClickHouse write + query in `inventory-service`
- Redis write + read in `inventory-service`
- RabbitMQ-based messaging in `inventory-service`

The important difference is the messaging client stack:

- `zero-code-inst`: direct `RabbitMQ.Client`
- `zero-code-masstransit`: `MassTransit`

## What we know so far

- `zero-code-inst` emits RabbitMQ-native spans with `messaging.system=rabbitmq`.
- Customer traces show MassTransit-specific attributes such as `messaging.masstransit.*`.
- `zero-code-masstransit` exists to answer a narrower question:
  does changing only the auto-instrumentation version change the MassTransit consumer span shape enough to help your parser?

## Version testing

Both zero-code manifests support a swappable auto-instrumentation version via:

```yaml
- name: OTEL_AUTO_VERSION
  value: v1.13.0
```

or in the MassTransit sample:

```yaml
- name: OTEL_AUTO_VERSION
  value: v1.9.0
```

That lets you compare:

1. raw `RabbitMQ.Client` on `v1.13.0`
2. `MassTransit` on `v1.9.0`
3. `MassTransit` on `v1.13.0`

without changing the overall deployment model.
