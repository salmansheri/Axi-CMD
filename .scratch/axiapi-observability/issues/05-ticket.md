# 5: Keep local telemetry across restarts with three-day retention

## What to build

Continue investigations across local restarts while limiting stored telemetry to three days.

## Acceptance criteria

- [ ] Configure persistent storage and three-day retention for Loki, Prometheus, and Tempo on the selected pinned stack version.
- [ ] Demonstrate request telemetry remains queryable after container restart.
- [ ] Verify retention configuration and cleanup with controlled aged data or accelerated test settings; document backend maintenance schedules.
- [ ] Keep published ports bound to localhost by default; explain deliberate configuration required for remote IIS connectivity.
- [ ] Provide storage locations, restart, reset, and cleanup instructions without automatically deleting developer data.

## Blocked by

- Ticket 1: Observe a normal AxiApi request in Grafana

## Status

Draft for breakdown approval; publish with ready-for-agent after approval.

