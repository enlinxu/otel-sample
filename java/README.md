# Java OpenTelemetry samples

This directory contains samples for auto-instrumenting Java applications with OpenTelemetry.

## Current samples

- [Auto-instrumentation with Java Agent 2.6.1](./auto-instr/README.md)
  - Zero-code approach using `opentelemetry-javaagent`
  - Java 17 with Spring Boot
  - HAProxy load balancer setup (mimics external LB)

## Why this directory

Many teams run legacy Java applications on Kubernetes behind external load balancers (HAProxy, F5, etc.). This directory provides copyable examples that show:

- how to attach the Java agent to running services
- how to ensure trace context propagates through external load balancers
- how to verify spans appear in Tempo/Grafana

## Quick start

1. Go to `java/auto-instr`
2. Follow the README to build, deploy to kind, and generate traffic
3. Verify traces in Tempo

## Java Agent vs Code-Based

| Dimension | Java Agent (auto-instr) | Code-Based |
|-----------|------------------------|------------|
| Code changes | None | Add SDK dependencies |
| Deploy changes | Add `JAVA_TOOL_OPTIONS` | Rebuild app |
| Control | Limited to env vars/config | Full control |
| Custom spans | Via config only | Direct in code |
| Best for | Legacy/3rd-party apps | Greenfield services |

## Prompt For Agent

Use this prompt with a coding agent when you want it to instrument a Java deployment by following this repo:

```text
Read the README and code under java/ in this repo before making changes.

Goal:
Enable OpenTelemetry tracing in my Java application with the least invasive approach first.

Instructions:
- Use the Java agent sample in this repo as the default reference.
- Reuse the same deployment pattern, JAVA_TOOL_OPTIONS usage, OTEL_SERVICE_NAME convention, and OTLP exporter environment variables.
- Preserve my application code unless there is a clear reason the Java agent is insufficient.
- Make sure trace propagation continues through any external load balancer, reverse proxy, or gateway in front of my services.
- Follow OpenTelemetry semantic conventions for standard dependency spans.
- Show me exactly which deployment files, env vars, and startup flags you changed.
- If the Java agent will not capture a required dependency or business operation, say so explicitly and propose the minimum code-based follow-up.

Expected outcome:
My Java deployment should emit traces structurally similar to the sample in this repo and be suitable for dependency-aware RCA.
```
