# otel-sample

Practical OpenTelemetry sample apps to help teams instrument applications and generate high-quality traces for RCA workflows.

## Why this repo exists

The intended use case is simple:

- a customer wants traces
- we point them to a working sample in their language
- they or their coding agent follow the sample and apply the same pattern in their real codebase
- the result is trace data that is usable for dependency-aware RCA

## Current samples

- [.NET samples](./dotnet/README.md)
  - [Code-based instrumentation](./dotnet/code-based-instr/README.md)
  - [Zero-code auto-instrumentation](./dotnet/zero-code-inst/README.md)
  - [Zero-code auto-instrumentation with MassTransit](./dotnet/zero-code-masstransit/README.md)
- [Java samples](./java/README.md)
  - [Auto-instrumentation with Java Agent](./java/auto-instr/README.md)
- [Go sample](./go/code-based-instr/README.md)
  - [Code-based instrumentation](./go/code-based-instr/README.md)
- [Meeting cheat sheet](./MEETING_CHEATSHEET.md)

## What these samples cover

This repository provides copyable, working examples that show how to instrument:

- server or RPC inbound traffic
- client outbound traffic
- database calls
- cache calls
- messaging calls
- service-to-service trace propagation

## How to use this repo with a coding agent

Each language README now includes a `Prompt For Agent` section.

Use it like this:

1. Pick the sample that matches the customer's language and deployment model.
2. Give the agent the prompt from that README.
3. Tell the agent to inspect the sample code before changing the customer's code.
4. Have the agent reproduce the same trace categories and semantic conventions in the customer's application.

## Quick start (.NET)

1. Go to `dotnet/code-based-instr`, `dotnet/zero-code-inst`, or `dotnet/zero-code-masstransit`.
2. Follow that folder's README to build, deploy to kind, generate traffic, and verify traces in Tempo.

## Quick start (Java)

1. Go to `java/auto-instr`.
2. Follow the README to build, deploy to kind, generate traffic, and verify traces in Tempo.

## Quick start (Go)

1. Go to `go/code-based-instr`.
2. Follow the README to build, deploy to kind, generate traffic, and verify traces in Tempo.
