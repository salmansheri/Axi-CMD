# 1: Observe a normal AxiApi request in Grafana

## What to build

Diagnose a normal request end to end through the local telemetry stack.

## Acceptance criteria

- [ ] Run a pinned local otel-lgtm stack with Collector, Loki, Prometheus, Tempo, and Grafana; retain existing AxiApi hosting and bind published ports to localhost.
- [ ] Enable configurable OTLP/HTTP tracing and metrics explicitly; default telemetry to disabled, local traces to 100%, and metric export to 15 seconds.
- [ ] A real HTTP request appears as a trace and contributes to provisioned request-rate, p95-latency, and HTTP-error panels using route templates.
- [ ] Exclude sensitive data and tenant identifiers from traces and metrics, including URLs, resource attributes, and query strings.
- [ ] Use bounded background export and finite timeouts; demonstrate normal responses with telemetry disabled and the Collector unreachable.
- [ ] Establish the HTTP-host/test-Collector harness, controlling dependencies through existing IAxExtend contracts where feasible; document assembly requirements and any limitations.
- [ ] Provide reproducible startup and endpoint-configuration instructions.

## Blocked by

None (can start immediately).

## Status

Draft for breakdown approval; publish with ready-for-agent after approval.

