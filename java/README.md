# Java OpenTelemetry Samples

This directory contains samples for auto-instrumenting Java applications with OpenTelemetry.

## Current Samples

- [Auto-instrumentation with Java Agent 2.6.1](./auto-instr/README.md)
  - Zero-code approach using `opentelemetry-javaagent`
  - Java 17 with Spring Boot
  - HAProxy load balancer setup (mimics external LB)

## Why This Directory

Many teams run legacy Java applications on Kubernetes behind external load balancers (HAProxy, F5, etc.). This directory provides copyable examples that show:

- How to attach the Java agent to running services
- How to ensure trace context propagates through external load balancers
- How to verify spans appear in Tempo/Grafana

## Quick Start

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