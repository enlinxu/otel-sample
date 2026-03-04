# otel-sample

Practical OpenTelemetry sample apps to help teams instrument applications and generate high-quality traces for RCA workflows.

## Current samples

- [.NET samples](./dotnet/README.md)
  - [Code-based instrumentation](./dotnet/code-based-instr/README.md)
  - [Zero-code auto-instrumentation](./dotnet/zero-code-inst/README.md)

## Why this repo

Many teams have production services without tracing. This repository provides copyable, working examples that show how to instrument:

- server/inbound traffic
- client/outbound traffic
- database calls
- service-to-service trace propagation

## Quick start (.NET)

1. Go to `dotnet/code-based-instr` or `dotnet/zero-code-inst`.
2. Follow that folder's README to build, deploy to kind, generate traffic, and verify traces in Tempo.

