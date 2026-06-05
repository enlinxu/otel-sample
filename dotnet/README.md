# .NET OpenTelemetry samples

This directory contains three implementations of the demo app:

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

## Prompt For Agent

Use this prompt with a coding agent when you want it to instrument a real .NET codebase by following this repo:

```text
Read the README and code under dotnet/ in this repo before making changes.

Goal:
Instrument my .NET application with OpenTelemetry so it produces production-usable traces for:
- inbound server traffic
- outbound client traffic
- database traffic
- cache traffic if applicable
- messaging traffic if applicable
- trace propagation across service boundaries

Instructions:
- First decide whether my app should follow the code-based sample, the zero-code sample, or the MassTransit zero-code sample.
- Reuse the same OpenTelemetry patterns, package choices, environment variables, and service naming conventions used in this repo.
- Follow OpenTelemetry semantic conventions instead of inventing custom attribute names for standard dependencies.
- If my app uses direct RabbitMQ.Client, follow dotnet/zero-code-inst or dotnet/code-based-instr as appropriate.
- If my app uses MassTransit, follow dotnet/zero-code-masstransit for messaging expectations.
- Preserve my existing business logic; only add the minimum instrumentation and configuration needed.
- Show me exactly which files you changed and why.
- If zero-code will not capture an important dependency correctly, say so explicitly and switch to code-based instrumentation for that dependency.

Expected outcome:
My app should emit traces that look structurally similar to the samples in this repo and are suitable for dependency-aware RCA.
```
