# 4: Diagnose cache outcomes and successful fallback

## What to build

Explain cache behavior and recognize successful degradation without confusing it with terminal failure.

## Acceptance criteria

- [ ] Observe cache operations for Dynamic Command Configuration, grammar loading, and applicable user-favourite paths without changing their existing behavior.
- [ ] Expose cache timing, hit, miss, force-refresh, successful fallback, and write-failure outcomes in traces, metrics, and provisioned dashboard panels.
- [ ] Verify controlled hit, miss, forced refresh, cache unavailability, and write failure through real HTTP requests.
- [ ] Distinguish successful fallback from terminal failure; accurately represent paths that fail rather than fall back.
- [ ] Exclude raw cache keys, usernames, tenant identifiers, and cached content from exported telemetry.
- [ ] Document external helper coverage and avoid duplicate automatic and manual instrumentation.

## Blocked by

- Ticket 1: Observe a normal AxiApi request in Grafana

## Status

Draft for breakdown approval; publish with ready-for-agent after approval.

