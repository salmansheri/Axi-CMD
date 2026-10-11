# AxiApi local observability with OpenTelemetry, Loki, Prometheus, and Tempo

## Problem Statement

Developers cannot reliably diagnose slow or failing AxiApi requests across HTTP handling, database access, and Redis caching. Existing Serilog output does not provide a unified view of request latency, dependency timing, retries, cache outcomes, and correlated traces. Database failures can return HTTP 400, making HTTP 5xx counts insufficient, while existing log fields can expose usernames, tenant identifiers, and cache keys if exported unchanged.

## Solution

Instrument AxiApi and provide a reproducible local observability stack using OpenTelemetry, Loki for logs, Prometheus for metrics, Tempo for traces, and Grafana for exploration. Developers can identify slow endpoints, locate time spent in database and cache operations, inspect retries and fallback, and navigate between a failed request's trace and logs. Telemetry excludes sensitive application data and remains optional so unavailable telemetry infrastructure does not break requests.

## User Stories

1. As an AxiApi developer, I want a reproducible local observability stack, so that I can diagnose requests without provisioning production infrastructure.
2. As an AxiApi developer, I want to retain the existing application hosting setup, so that observability does not require containerizing AxiApi.
3. As an AxiApi developer, I want a configurable Collector endpoint, so that my application can reach the local backend from its execution environment.
4. As an AxiApi developer, I want telemetry disabled unless explicitly configured, so that existing installations do not start exporting data unexpectedly.
5. As an AxiApi developer, I want request rate by route, so that I can understand endpoint activity.
6. As an AxiApi developer, I want p95 request latency by route, so that I can identify slow endpoints.
7. As an AxiApi developer, I want HTTP errors visible, so that I can locate failing requests.
8. As an AxiApi developer, I want dependency failures counted independently of HTTP status, so that a database failure returned as HTTP 400 remains visible.
9. As an AxiApi developer, I want database and cache operation durations, so that I can distinguish application time from dependency time.
10. As an AxiApi developer, I want database retry attempts visible, so that recovered transient failures are distinguishable from exhausted operations.
11. As an AxiApi developer, I want cache hits and misses distinguishable, so that I can understand cache effectiveness.
12. As an AxiApi developer, I want force-refresh activity distinguishable from ordinary misses, so that intentional cache bypass does not mislead diagnosis.
13. As an AxiApi developer, I want successful cache fallback visible, so that I can recognize degraded behavior even when the request succeeds.
14. As an AxiApi developer, I want cache write failures visible, so that I can diagnose cache population problems.
15. As an AxiApi developer, I want correlated traces and logs, so that I can inspect a failed request without searching unrelated log entries.
16. As an AxiApi developer, I want every local request traced, so that I can inspect a reproducible failure without sampling uncertainty.
17. As an AxiApi developer, I want useful application logs with reduced framework noise, so that relevant events are easy to find.
18. As an AxiApi developer, I want collection settings configurable, so that I can adapt diagnostic detail to my local workload.
19. As an application owner, I want sensitive payloads and identifiers excluded from telemetry, so that diagnostics do not create an additional store of customer data.
20. As an application owner, I want exception content and URLs covered by the data boundary, so that indirect leakage does not bypass field filtering.
21. As an AxiApi developer, I want API requests to continue during Collector downtime, so that observability availability does not determine application availability.
22. As an AxiApi developer, I want bounded export buffers, so that backend downtime cannot cause unbounded application memory growth.
23. As an AxiApi developer, I want local telemetry to survive container restarts, so that I can continue an investigation across sessions.
24. As an AxiApi developer, I want three-day local retention, so that recent investigations remain available without indefinite storage growth.
25. As an AxiApi developer, I want stack ports restricted to localhost by default, so that local diagnostics are not exposed to the surrounding network.
26. As an AxiApi developer, I want a provisioned dashboard, so that I can start investigating without manually configuring panels and data sources.
27. As an AxiApi developer, I want controlled failure demonstrations and documented coverage limitations, so that I know what telemetry can reliably explain.
28. As an AxiApi developer, I want existing Serilog local logging retained, so that the feature fits the current diagnostic workflow.

## Implementation Decisions

- Keep AxiApi on its existing .NET 8 execution and hosting model, including documented Windows IIS deployments. Run the local Collector and backends separately using a pinned, verified `grafana/otel-lgtm` image.
- Use Loki, Prometheus, Tempo, and Grafana. Send logs, metrics, and traces through OTLP/HTTP to the Collector. Configure the Collector's signal routing to the selected backends.
- Add observability registration and configuration at application startup. Keep telemetry disabled unless explicitly configured; expose endpoint and collection settings through application configuration.
- Use background export with bounded buffers and finite export timeouts. Collector unavailability must not fail API requests; full buffers may drop telemetry. Do not promise lossless delivery during outages.
- Capture HTTP request traces and route-level request rate, duration, and error metrics. Use route templates rather than raw URLs or identifiers as dimensions.
- Instrument database and cache operations where AxiApi calls AxExtend/ARMCommon, including existing retry attempts. Verify deeper automatic instrumentation before enabling it; avoid duplicate operation counts and spans.
- Do not add retries or change cache behavior as part of instrumentation. Observe actual behavior, including paths that fail instead of falling back.
- Record dependency outcome independently of HTTP status. Distinguish retry attempts, terminal failures, cache hits, misses, force-refresh, fallback, and write failures using bounded operation/outcome values. Successful fallback must not mark the completed request as a terminal failure.
- Retain Serilog local logging and add correlated exported logs. Clean sensitive fields and exception content before export; prevent duplicate exported log events.
- Exclude request/response bodies, SQL values, credentials, usernames, raw command text, and tenant identifiers including `appname`. Apply the boundary to attributes, messages, exception content, cache keys, URLs, and resource fields. Raw query strings, SQL text, and cache keys must not provide an indirect route for excluded values.
- Use 100% trace collection for local development, Information-and-above application logs, Warning-and-above noisy framework logs, and a 15-second metric export interval. Make these configurable; production defaults are outside this milestone.
- Configure three-day retention for all three backends and persistent storage across container restarts. Verify image configuration rather than relying on defaults. Retention cleanup may follow backend maintenance schedules and must be documented.
- Bind published local ports to localhost. Remote IIS connectivity requires deliberate endpoint/network configuration rather than publicly exposing the stack by default.
- Provision one AxiApi dashboard with request rate, p95 latency, HTTP errors, dependency failures/duration, retries, and cache outcomes. Provision data sources and navigation between Tempo traces and Loki logs using correlation identifiers.
- Preserve endpoint routes, response contracts, database schema, and domain operations. Observability must not silently change HTTP 400 database-error mappings.
- Provide setup, configuration, controlled failure validation, storage/reset guidance, and instrumentation limitations for developers.

## Testing Decisions

- Prefer one application-level test entry point: send requests through real AxiApi HTTP endpoints and inspect response behavior together with telemetry received at a test Collector. Assert externally observable behavior rather than instrumentation class names, private calls, or exact span counts.
- Control database and cache scenarios through the existing injected IAxExtend boundary and its helper contracts while keeping services, repositories, and exception handling real. This boundary sits below the behavior whose retries and fallback must be observed. If external assembly contracts prevent substitution, document the limitation and use controlled integration dependencies rather than adding scattered production test hooks.
- Add only the application-host accessibility needed by the test harness if the current startup structure requires it. Use a small set of representative endpoint flows covering Dynamic Command Configuration, grammar loading, and keyfield database retries.
- No .NET test project or comparable AxiApi integration suite was found during repository exploration. Establish a minimal HTTP integration harness rather than claiming existing test infrastructure.
- Verify a normal request produces a trace and HTTP metrics; verify cache hit, miss, and force-refresh outcomes; verify successful fallback and cache write failure; verify existing retry attempts and terminal database failure, including HTTP 400 responses.
- Verify trace/log correlation identifiers and test privacy with recognizable sentinel values in query parameters, bodies, tenant names, usernames, cache keys, SQL values, and exception messages. Assert exclusion across every exported signal.
- Verify disabled telemetry preserves application behavior. With export enabled, stop or make the Collector unreachable and verify responses still complete and buffers remain bounded. Use bounded waits for asynchronous export rather than brittle sleeps or arbitrary performance targets.
- Run real-stack smoke checks for Collector routing into Loki/Prometheus/Tempo, provisioned dashboard queries, trace/log navigation, localhost bindings, and persistence across restart. Validate retention configuration and backend cleanup behavior using controlled aged data or an accelerated test configuration rather than waiting three days.
- Use controlled test dependencies and data for failure injection; do not require changing shared customer infrastructure. Record external-library limitations and distinguish boundary timings from database-server execution timings.

## Out of Scope

Production deployment and hardening, production sampling and retention, remote-network access design, tenant filtering, alert notifications and ownership/thresholds, browser instrumentation, business analytics, profiling, API response redesign, database schema changes, and changes to retry or fallback policies.

## Further Notes

The design rounds were accepted by the user, who subsequently authorized implementation using the proposed HTTP-host/IAxExtend/test-Collector approach. The local all-in-one image is a development tool, not a production deployment prescription. External AxExtend/ARMCommon assemblies are required for building and realistic integration coverage and may constrain automatic instrumentation. Follow the recorded telemetry data-boundary ADR.
