# 6: Validate the complete observability workflow during failures

## What to build

Verify the full diagnostic workflow remains useful, private, and non-disruptive during controlled failures.

## Acceptance criteria

- [ ] Demonstrate the complete provisioned dashboard: request rate, p95 latency, HTTP errors, dependency failures/duration, retries, cache outcomes, and trace/log navigation.
- [ ] Run a controlled acceptance matrix covering normal requests, cache hit/miss/force-refresh, successful fallback, write failure, database retry, and terminal database failure.
- [ ] Verify sensitive sentinel values remain absent across all three signals and all instrumented scenarios, including nested exception content and URLs.
- [ ] Demonstrate continued API operation during Collector downtime and bounded exporter buffers under sustained outage, including finite export waits and permitted telemetry drops.
- [ ] Verify configurable collection defaults, disabled telemetry behavior, localhost bindings, restart persistence, and retention smoke checks.
- [ ] Deliver a reproducible developer runbook and document external-library limitations; preserve existing API and domain behavior.
- [ ] Keep production hosting, alert notifications, tenant filtering, browser instrumentation, business analytics, and profiling out of scope.

## Blocked by

- Ticket 2: Navigate from AxiApi traces to safe correlated logs
- Ticket 3: Diagnose database latency, retries, and terminal failures
- Ticket 4: Diagnose cache outcomes and successful fallback
- Ticket 5: Keep local telemetry across restarts with three-day retention

## Status

Draft for breakdown approval; publish with ready-for-agent after approval.

