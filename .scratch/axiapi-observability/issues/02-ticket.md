# 2: Navigate from AxiApi traces to safe correlated logs

## What to build

Investigate a failed request using correlated logs and traces without exporting sensitive data.

## Acceptance criteria

- [ ] Export application logs to Loki through OTLP/HTTP while retaining Serilog local logging and avoiding duplicate export.
- [ ] Provide trace-to-log and log-to-trace navigation in provisioned Grafana data sources.
- [ ] Default application logging to Information and noisy framework logging to Warning; allow configuration.
- [ ] Audit and clean exported messages, structured fields, exception content, cache keys, SQL content, and URLs against the agreed data boundary.
- [ ] Use sentinel values for usernames, tenant identifiers, credentials, command text, SQL values, and payloads to verify exclusion across exported logs and correlation attributes.
- [ ] A controlled failing HTTP request can be inspected through its trace and correlated safe logs.

## Blocked by

- Ticket 1: Observe a normal AxiApi request in Grafana

## Status

Draft for breakdown approval; publish with ready-for-agent after approval.

