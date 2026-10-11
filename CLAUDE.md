# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this repo is

The Axpert Web shell (legacy ASP.NET WebForms, .NET Framework, IIS) with the **Axi Command Palette** (`AxiCMD`) plugin on top. The work in this repo is almost entirely Axi: a front-end command palette plus the `AxiApi` backend. Read `CONTEXT.md` (domain glossary) and `docs/adr/` before working in an area, and use the glossary's terms. Agent-skill conventions live in `docs/agents/` (issues are GitHub issues via `gh`).

## Architecture

Three tiers (see `AxpertPlugins/AxiCMD/README.md`, `AxiApi/README.md`):

- **Front end** — `AxpertPlugins/AxiCMD/` (`axicmdmain.js`, `Smartview.html`, `PopupContainer.html`, `PluginScripts/mainHomeConfigTemplate.html`, `AxInterface.js`). `mainHomeConfigTemplate.html` hosts the `AXI-Sec` palette in the Axpert shell. Several files are also copied into the root `Js/` and `aspx/` dirs (e.g. `AxInterface.js`, `axinterface.aspx`). Edit the matching copies deliberately; the `*_bkp_*`/`_old` files and `__bkp/` are stale backups.
- **AxiApi** (`AxiApi/`, .NET 8 ASP.NET Core, runs as an IIS app in the ARM microservices environment) — `AxiController` (`/api/v1/Axi/...`) → services → repositories → DB. All DB access goes through `IAxExtend` / `ARMCommon` (closed-source Axpert DLLs, not NuGet) rather than raw Npgsql. SQL is centralized in `Lib/SqlQueries.cs`. Repositories retry transient connection-reset errors. DI wiring is in `Program.cs` (all scoped).
- **Data** — PostgreSQL (or Oracle) tenant DBs, with Redis caching under `{appname}-command_config-` style keys. Redis outage falls back to the DB for grammar and command-config, but **user-favourite reads fail** without Redis. Command routing is database-driven (`axi_command_config` table), not hardcoded client-side.
- **Observability** (`AxiApi/Observability/`, `AxiApi/OBSERVABILITY.md`) — OpenTelemetry, disabled by default, enabled with `Observability__Enabled=true` and `Observability__Endpoint=http://localhost:4318`. ADR 0001 forbids exporting bodies, SQL values, usernames, raw command text, or tenant ids (`appname`) as telemetry; keep new instrumentation to route templates and fixed operation names.

The root WebForms app (`AxpertWeb.sln`, `Default.aspx`, `Web.config`, `Bin/`) is Windows/IIS only and cannot be built or run on Linux.

## Commands (AxiApi, from repo root)

`AxiApi.csproj` and `AxiApi.Tests.csproj` expect `ARMCommon.dll` and `AxExtend.dll` at `../../ARMMICRO/AxPlugins/AxExtend`, which is absent on some machines (e.g. this Linux box). Without it the build fails with ~49 `CS0246` errors for `IAxExtend`/`NonQueryResult`. Override with a copy checked into the repo:

```bash
export AxRuntimePath=$PWD/AxpertPlugins/AxiCMD/PluginScripts/AxiCMDApi

dotnet build AxiApi/AxiApi.sln -p:AxRuntimePath=$AxRuntimePath
dotnet run --project AxiApi/AxiApi.csproj          # http://localhost:5057/swagger
dotnet test AxiApi.Tests/AxiApi.Tests.csproj -p:AxRuntimePath=$AxRuntimePath
dotnet test AxiApi.Tests/AxiApi.Tests.csproj --filter "FullyQualifiedName~ObservabilityTests"   # subset / single test
```

Local observability stack (Grafana on :3000, OTLP/HTTP on :4318): `docker compose -f AxiApi/observability-stack/compose.yaml up -d`.

Redis (`localhost:6379` in `appsettings.Development.json`) is optional for most paths. Tests are xUnit + Moq + `WebApplicationFactory`; the only test file is `ObservabilityTests.cs`. There is no lint config.

## Gotchas

- Paths and `.ini` files (`AxPaths.ini`, `ConfigAuthentication.ini`) were authored on Windows (`D:\Axpert11.4\...`); expect case-sensitivity and backslash problems on Linux.
- `ContextAutocompleteService.cs` and `ContextAutocompleteService1.cs` coexist in `AxiApi/Services/`; confirm which one is registered before editing.
- `WeatherForecast*` in AxiApi is template leftovers.
