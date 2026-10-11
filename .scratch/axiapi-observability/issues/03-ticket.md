# 3: Diagnose database latency, retries, and terminal failures

## What to build

Explain database latency and distinguish recovered retries from terminal failures.

## Acceptance criteria

- [ ] Instrument AxiApi database boundaries across repositories and existing retry paths without changing SQL, retry policies, or response contracts.
- [ ] Show dependency duration, outcomes, retry attempts, and terminal failures through exported telemetry and provisioned dashboard panels.
- [ ] Demonstrate a successful operation, a recovered retry, and exhausted or terminal failure using controlled dependencies.
- [ ] Count terminal dependency failures independently of HTTP status, including existing HTTP 400 mappings; avoid double-counting attempts as terminal failures.
- [ ] Keep dimensions bounded and exclude tenant identifiers, SQL values, connection strings, and raw exception data.
- [ ] Verify deeper automatic instrumentation before enabling it, avoid duplicate spans/counts, and document boundary timing versus server execution coverage.

## Blocked by

- Ticket 1: Observe a normal AxiApi request in Grafana

## Status

Draft for breakdown approval; publish with ready-for-agent after approval.

