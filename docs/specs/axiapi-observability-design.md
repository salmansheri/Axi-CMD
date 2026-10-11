# AxiApi observability design

All interview rounds were agreed on 2026-10-11. The user subsequently authorized implementation of the spec and draft ticket breakdown.

## Settled decisions

- First milestone: instrument AxiApi and validate locally with `grafana/otel-lgtm`. Production hosting is a separate milestone.
- Diagnose slow and failing endpoints, distinguish database and cache time, and navigate from a failed request trace to correlated logs.
- Include HTTP latency and error metrics, dependency traces, correlated logs, database retry visibility, and cache fallback visibility.
- Defer browser instrumentation and business analytics.
- Backends: Loki for logs, Prometheus for metrics, Tempo for traces, and Grafana for exploration and dashboards.
- Exclude request and response bodies, SQL values, credentials, usernames, and raw command text from telemetry by default.
- Keep AxiApi's existing execution setup; run the local backend separately in Docker and configure a reachable telemetry endpoint.
- Instrument database and cache operations at AxiApi's AxExtend/ARMCommon boundaries, including retry attempts. Enable deeper automatic instrumentation only where verified.
- Clean sensitive log fields and exception content before exporting logs; retain Serilog local logging.
- Omit tenant identifiers, including `appname`, from exported telemetry in the first milestone.
- Count dependency failures independently of HTTP status. Distinguish retry attempts and successful cache fallback from terminal failures.
- Export all three signals using OTLP/HTTP to the Collector, with bounded background export. Telemetry is disabled unless explicitly configured; Collector downtime must not fail API requests. Full buffers may drop telemetry.
- Local development captures 100% of traces, Information-and-above application logs, Warning-and-above noisy framework logs, and metrics every 15 seconds. These settings are configurable; production sampling is deferred.
- Local telemetry retention is three days, with persistence across container restarts and host ports bound to localhost by default. Remote IIS connectivity requires explicit network configuration.
- Provision one AxiApi dashboard covering request rate, p95 latency, HTTP errors, dependency failures and duration, retries, and cache outcomes, with trace/log navigation. Alert notifications are deferred until production ownership and thresholds are established.

## Telemetry flow

```text
AxiApi (.NET 8; existing hosting)
  -> OTLP/HTTP -> OpenTelemetry Collector
                   -> Loki (logs)
                   -> Prometheus (metrics)
                   -> Tempo (traces)
                 Grafana (dashboard and trace/log exploration)
```

The Collector and backends run in the local `grafana/otel-lgtm` Docker stack. Configure and verify the selected image version's backend retention and persistent storage; an image's defaults do not establish the three-day requirement.

## Acceptance criteria

- A normal request produces HTTP metrics and a trace.
- Cache hits and misses are distinguishable; successful fallback is visible without classifying the completed request as a terminal dependency failure.
- A controlled database retry is visible, and exhausted/terminal database failures are counted even when HTTP status is 400.
- A failed request's trace and exported logs are linked in Grafana.
- Exported telemetry excludes the agreed sensitive data and tenant identifiers, including values nested in exception content, cache keys, and URLs.
- Collector downtime does not fail API requests; export buffers remain bounded.
- Data survives a container restart and all three backends enforce the agreed retention.
- Failure scenarios are controlled and external-library instrumentation limitations are documented.

## Findings that constrain the design

- AxiApi targets .NET 8 and already routes logging through Serilog.
- Deployment documentation describes Windows IIS; the local telemetry backend must not imply containerizing AxiApi.
- Database and cache access passes through external AxExtend/ARMCommon assemblies. Automatic instrumentation coverage needs verification.
- Existing logs include usernames and cache keys; forwarding them unchanged would violate the agreed data boundary.
- The exception handler maps database failures to HTTP 400. HTTP 5xx metrics alone cannot represent dependency failures.

## Confirmation and deferred work

No design interview questions remain. Implementation is authorized.

Production hosting, sampling, retention, access control, tenant filtering, alert ownership and thresholds, browser instrumentation, and business analytics are separate future work.

## References

- [Grafana docker-otel-lgtm](https://github.com/grafana/docker-otel-lgtm): intended for development, demo, and testing environments.
- [OpenTelemetry .NET](https://opentelemetry.io/docs/languages/dotnet/): application telemetry APIs and SDKs.
