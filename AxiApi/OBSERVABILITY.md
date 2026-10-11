# Local AxiApi observability

AxiApi exports OpenTelemetry traces, metrics, and safe log summaries to a local Collector over OTLP/HTTP. Grafana explores Loki logs, Prometheus metrics, and Tempo traces. AxiApi keeps its existing .NET 8/IIS hosting model; only the development backend runs in Docker.

## Start and configure

From the repository root:

```bash
docker compose -f AxiApi/observability-stack/compose.yaml up -d
docker compose -f AxiApi/observability-stack/compose.yaml ps
```

Open [the AxiApi dashboard](http://localhost:3000/d/axiapi-observability/axiapi-observability). Grafana permits anonymous access in this local development image. Only Grafana port 3000 and OTLP/HTTP port 4318 are published, both on localhost. This is a development stack, not a production hosting configuration.

Telemetry is disabled by default. Merge the `Observability` section from the example settings into application configuration, or use environment variables:

```bash
Observability__Enabled=true Observability__Endpoint=http://localhost:4318 \
  dotnet run --project AxiApi/AxiApi.csproj
```

PowerShell:

```powershell
$env:Observability__Enabled = "true"
$env:Observability__Endpoint = "http://localhost:4318"
dotnet run --project AxiApi/AxiApi.csproj
```

For IIS, configure these settings in the application environment and restart the application pool. The Collector base URL must be reachable from the application host. A stack on a different machine requires an explicit networking/access-control design; localhost bindings do not expose it remotely.

The example settings file is a reference and is not loaded automatically. Default collection settings are 100% trace sampling, a 15-second metric export interval, Information application logs, and Warning framework logs. Trace ratio, log levels, export timeout, batch delay, queue size, and batch size are configurable. Export queues are bounded; an unavailable Collector does not fail requests, and full queues may drop telemetry. Collection does not promise lossless delivery during outages.

The project requires matching `ARMCommon.dll` and `AxExtend.dll`. The normal runtime directory remains the default. To build against another existing runtime directory, pass an absolute override:

```bash
dotnet build AxiApi/AxiApi.sln -p:AxRuntimePath=/absolute/path/to/AxExtend
```

## What to inspect

The dashboard shows request rate, p95 request and dependency latency, HTTP errors, terminal dependency failures, database retries, and cache outcomes. Route templates and fixed operation names keep dimensions bounded. Open a trace ID in Loki to inspect its Tempo trace; Tempo's logs link selects the same trace ID in Loki.

Dependency call errors and terminal operation failures are different. A recovered database retry contributes to the retry count and failed call count, but not the terminal failure count. A successful cache fallback has its own outcome and does not mark the request as a terminal cache failure. Database failures remain visible even when the current API returns HTTP 400.

Instrumentation observes existing behavior. Grammar and Dynamic Command Configuration can fall back to the database; user-favourite reads currently fail when Redis is unavailable. Keyfield operations retain their existing retry policy. No response contracts, schemas, retries, or fallback policies are changed.

## Data boundary and coverage

Exported telemetry excludes bodies, SQL text/values, credentials, usernames, raw commands, raw cache keys, raw URLs/query strings, and tenant identifiers including `appname`. Incoming trace IDs remain usable for distributed correlation; caller-controlled `tracestate` and baggage values are not exported.

The Serilog exporter emits allowlisted event summaries, severity, and validated fixed dependency/operation/outcome fields. It excludes original messages, arbitrary properties, scopes, and exceptions. Detailed existing Serilog local output remains controlled by its existing configuration. Unrecognized log events receive a generic summary. Exported resources use the fixed service name `AxiApi` and omit host/tenant identifiers.

Database/cache spans measure the calls AxiApi makes at its AxExtend/ARMCommon boundaries. They include connection and operation time, not isolated database-server execution time. Automatic instrumentation inside those external assemblies is not enabled without verified coverage. HTTP telemetry uses the application middleware and OpenTelemetry API/SDK; no automatic capture of payloads or SQL is enabled. The all-in-one image also starts its bundled Pyroscope process, but AxiApi does not export profiles.

## Verify behavior

The integration suite drives real HTTP endpoints, controls external dependencies through IAxExtend, and asserts OTLP received at a test Collector. It covers cache outcomes, retries, HTTP 400 dependency failures, privacy, trace/log correlation, disabled telemetry, and Collector outages. Tests use private sentinel strings rather than customer infrastructure.

```bash
dotnet test AxiApi/AxiApi.sln -p:AxRuntimePath=/absolute/path/to/AxExtend
```

For a real-backend smoke check, forward controlled test telemetry to the running stack:

```bash
AXI_TEST_FORWARD_ENDPOINT=http://localhost:4318 \
  dotnet test AxiApi.Tests/AxiApi.Tests.csproj \
  -p:AxRuntimePath=/absolute/path/to/AxExtend \
  --filter 'Normal_request|Database_failure|Command_configuration_cache|Database_retries'
python3 AxiApi/observability-stack/verify-stack.py
```

After successful verification, restart and re-run the verifier to confirm persistence:

```bash
docker compose -f AxiApi/observability-stack/compose.yaml restart
python3 AxiApi/observability-stack/verify-stack.py
```

Wait for the container to become healthy before checking it. Backend queries and log visibility are eventually consistent.

## Retention and storage

The named `axiapi-observability_telemetry` volume persists `/data`, including backend and Grafana state. All three telemetry backends are configured for three-day retention. Prometheus deletes expired TSDB blocks during maintenance; [Loki retention](https://grafana.com/docs/loki/latest/operations/storage/retention/) runs during compaction (10-minute interval here) and delays physical deletion by two hours; Tempo schedules retention work every 10 minutes. Cleanup operates on blocks/chunks, and cached query data may remain visible longer, so three days is a retention policy rather than a precise per-record deletion deadline.

The stack verifier checks effective retention settings. Run the separate cleanup verifier to exercise deletion with controlled aged fixtures:

```bash
python3 AxiApi/observability-stack/verify-retention.py
```

It starts the same pinned image in a temporary data directory with no published ports, keeps the three-day policy, and shortens maintenance/cache intervals. Prometheus receives aged and fresh TSDB fixtures; Loki receives aged and fresh logs; Tempo receives a real trace block whose metadata is aged in the isolated fixture. The explicitly flushed Loki fixtures use no WAL replay, preventing the injected old log from being re-ingested after deletion. It checks physical deletion and query disappearance with fresh-data controls, restarting the isolated backends to reload aged metadata and clear stale query caches. It then removes its own container and files. This takes several minutes and never accesses the developer stack's volume. Never use shared/customer backends for these fixtures.

Stop the stack without deleting data:

```bash
docker compose -f AxiApi/observability-stack/compose.yaml down
```

To deliberately erase local telemetry and Grafana state, run `docker compose -f AxiApi/observability-stack/compose.yaml down -v`. Ordinary startup, verification, and restart commands never erase the volume.

Configuration is pinned to [grafana/otel-lgtm v0.36.0](https://github.com/grafana/docker-otel-lgtm/tree/v0.36.0) and its image digest. Tempo 3 retention uses its [current override/scheduler configuration](https://grafana.com/docs/tempo/latest/configuration/). Production hosting, access control, alert notifications, tenant filtering, browser instrumentation, and business analytics remain separate work.
