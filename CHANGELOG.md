# Changelog

All notable changes to ElsaBroker are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project aims to
adhere to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

_(nothing yet)_

## [0.4.0] — 2026-06-08

> Switchboard — a Blazor Server + Fluent UI monitoring dashboard for the ElsaBroker pipeline.
> Events persisted to SQLite via `SwitchboardLogShipper`; ingested live from Queue/Processor via
> HTTP. Overview, Events, and Faults pages. Ships as a Docker service under the `"dashboard"` profile.
> See [release/v0.4.0.md](docs/agile/release/v0.4.0.md) for the full release report.

### Added

- **`ElsaBroker.Switchboard` project** — Blazor Server dashboard using Fluent UI v4
  (`Microsoft.FluentUI.AspNetCore.Components`). Targets `net9.0`; depends on
  `ElsaBroker.Abstractions` for `ILogShipper` / `BrokerLogEvent`.
- **`SwitchboardDbContext`** — EF Core + SQLite. Auto-creates `switchboard.db` on first run; path
  configurable via `Switchboard:DatabasePath`. `LogEventRecord` entity indexed on Timestamp,
  CorrelationId, EventType, Level.
- **`SwitchboardLogShipper`** — implements `ILogShipper`; persists `BrokerLogEvent` records to
  SQLite. Plug in by pointing `HttpJsonLogShipper` in Queue/Processor at Switchboard's port.
- **`POST /api/events`** — HTTP ingestion endpoint; accepts `BrokerLogEvent` JSON for live event
  shipping from Queue and Processor.
- **Overview page** — metric cards (Submitted / Processing / Completed / Faulted), status funnel,
  throughput chart (last 24 h in hourly buckets), request-type breakdown table, recent activity grid.
- **Events page** — filterable event stream (EventType, Level, time range, free-text search); row
  click opens a detail dialog.
- **Faults page** — error/fault event list; row click opens exception detail drill-down dialog with
  full stack trace.
- **`SwitchboardSeeder`** — development-mode seed data; ~78 realistic correlated log events covering
  multiple clients, request types, and faulted events with stack traces.
- **`ElsaBroker.Switchboard/Dockerfile`** — multi-stage .NET 9 build; exposes port 5002.
- **docker-compose `switchboard` service** — under the `"dashboard"` profile on port 5002; depends
  on `sqlserver`.

### Fixed

- **SQLite `DateTimeOffset` limitation** — all timestamp-filtered queries in `SwitchboardDbContext`
  materialize before applying `DateTimeOffset` predicates, avoiding EF Core SQLite provider
  translation errors.
- **Fluent UI styling** — added `FluentDesignTheme` component and CSS isolation bundle link; absence
  caused unstyled component output on first run.

## [0.3.0] — 2026-06-08

> NuGet packaging and Docker deployment artifacts. Package boundaries settled by ADR-0008
> (three-package Option B). Queue and Processor gain production-quality Dockerfiles; a five-service
> reference compose and deployment quickstart complete the arc toward the `1.0.0` public launch.
> See [release/v0.3.0.md](docs/agile/release/v0.3.0.md) for the full release report.

### Added

- **ADR-0008** — NuGet package boundaries. Evaluated three options; adopted Option B (three packages):
  `ElsaBroker.Contracts`, `ElsaBroker.Abstractions`, `ElsaBroker`. Includes public API surface table
  (22+ types), package dependency diagram, and 5-step confirmation checklist.
  ([ADR-0008](docs/adr/0008-nuget-package-boundaries.md))
- **`ElsaBroker.Abstractions` project** — new class library containing `ILogShipper`, `BrokerLogEvent`,
  `IRequestHandler`, `RequestResult` extracted from `ElsaBroker.Data.Logging` and
  `ElsaBroker.Processor.Handlers`. Depends only on `ElsaBroker.Contracts`.
- **`dotnet pack` — three NuGet packages:** `ElsaBroker.Contracts.0.3.0.nupkg` (6.9 KB),
  `ElsaBroker.Abstractions.0.3.0.nupkg` (10.9 KB), `ElsaBroker.0.3.0.nupkg` (33.7 KB).
- **`ElsaBroker.Queue/Dockerfile`** — multi-stage .NET 9 build; exposes ports 5001 and 5080; bakes
  `requestTypes.json` and `ClientAllowlist.json`.
- **`ElsaBroker.Processor/Dockerfile`** — multi-stage .NET 9 build; bakes `workflows/`; no exposed ports.
- **`docker-compose.reference.yml`** — production-like five-service reference stack (`sqlserver`,
  `queue`, `processor`, `elsa-server`, `elsa-studio`) with healthchecks, env-var parameterization,
  and an internal Docker network. Distinct from the dev-lab `docker-compose.yml`.
- **`docs/articles/deploy-elsa-broker.md`** — deployment quickstart covering NuGet package roles,
  Docker deployment, env vars, certs, and configuration reference. Wired into `docs/toc.yml`.

### Changed

- **`Directory.Build.props`** — NuGet packaging metadata added: Version, Authors, License MIT,
  RepositoryUrl. `IsPackable=false` default; library projects opt in explicitly.
- **`.dockerignore`** — updated to remove per-project exclusions that would have broken the shared
  multi-Dockerfile build context.

### Fixed

- **Solution file** — added missing `Release|Any CPU` build configuration mappings for all original
  projects. These were absent from the initial scaffold; `dotnet pack` under Release configuration
  would silently skip affected projects without this fix.

## [0.2.0] — 2026-06-08

> Structured logging foundation and pluggable shipper abstraction. Also formally promotes the
> EB-001/005/006 work (ADR-0007, containerized lab, docs rewrite) that shipped in the v0.1.0 sprint
> but was recorded in `[Unreleased]`. See [release/v0.2.0.md](docs/agile/release/v0.2.0.md) for the
> full release report.

### Added

- **`ILogShipper` abstraction** (`ElsaBroker.Data/Logging/`) — pluggable, config-driven structured
  log shipping. Implementations: `NullLogShipper` (no-op default), `ConsoleLogShipper` (JSON
  stdout), `HttpJsonLogShipper` (HTTP POST to Seq / Logstash / OpenSearch), `CompositeLogShipper`
  (fan-out). DI registration via `LogShipperExtensions`; sink selected by `LogShipper:Sink` in
  `appsettings.json`.
- **`BrokerLogEvent`** structured event record — Timestamp, EventType, Level, Source, CorrelationId,
  ClientId, RequestType, Message, Properties, Exception.
- **Queue log events:** `RequestSubmitted` (POST /requests), `CallbackReceived` (callback endpoint).
- **Processor log events:** `RequestProcessing`, `RequestFaulted` (no handler), `RequestDispatched`
  (deferred to Elsa), `RequestCompleted` (sync handler), `RequestFaulted` (exception). Five lifecycle
  points correlated by `CorrelationId`.
- **Seq compose profile** — `docker compose --profile logging up -d` adds Seq (ingestion `:5341`,
  UI `:8081`). `HttpJsonLogShipper` targets it when `LogShipper:Sink` is `"seq"`.
- **Docs: structured logging article** (`docs/articles/structured-logging.md`) — all 6 event types
  with field reference, sink configuration table, custom-shipper extension point with example.
  Wired into `docs/toc.yml`.
- **ADR-0007** — architecture decision: keep the custom MassTransit topology (Queue + Processor +
  HTTP dispatch to Elsa) over Elsa's built-in dispatch. SWOT analysis, needs-mapping table, and
  explicit recommendation. Gates EB-002/003/004 cleared.
  ([ADR-0007](docs/adr/0007-custom-masstransit-topology-vs-elsa-built-in-dispatch.md))
- **Dockerfile** for `ElsaBroker.WorkflowServer` — multi-stage .NET 9 build, native arm64 on Apple
  Silicon (no amd64 pin).
- **Three-service docker-compose** — `sqlserver` + `elsa-server` (custom build) + `elsa-studio`
  (official image). Replaces the single demo image. `docker compose up -d` produces a one-command lab.

### Changed

- **Docs rewrite** — getting-started, elsa-integration, and elsa-deployment articles updated for the
  custom `ElsaBroker.WorkflowServer` (not the retired demo image). Three integration gotchas
  documented (`PostAsJsonAsync` chunked encoding, JS header parentheses, demo image
  MassTransit/WaitForCompletion).
- **Test harness** (`ElsaBroker.Tests`) updated to register `NullLogShipper` — required after
  `SubmitRequestConsumer` constructor was extended to accept `ILogShipper`. All 16/16 tests pass,
  0 warnings, 0 errors.

## [0.1.0] — 2026-06-08

> First formal, versioned release of ElsaBroker under the agile process. Codifies the msg-broker →
> elsa-broker pivot and the Elsa 3 integration that was already complete and validated end-to-end on
> 2026-06-06, sitting in `[Unreleased]` until now. See
> [release/v0.1.0.md](docs/agile/release/v0.1.0.md) for the full release report.

### Changed — pivot to elsa-broker (2026-06-07)
- **Renamed `msg-broker` → `elsa-broker`**, including the .NET solution and all namespaces
  (`MsgBroker.*` → `ElsaBroker.*`), the public slug (`/elsa-broker`, `dist/elsa-broker`,
  `make build-elsa-broker`), and the website card. The broker is now an **Elsa 3 tool**: a durable
  mTLS queueing front end whose processing is **Elsa workflows**, not C# handlers.

### Added — Elsa 3 integration
- **Shared `workflows/` folder + convention.** Elsa 3 JSON definitions live in
  `src/services/ElsaBroker/workflows/`; each request-handling workflow declares
  `customProperties.requestType`. `WorkflowFolderScanner` + `WorkflowRegistry` scan the same folder the
  Elsa server loads and auto-register `requestType → definitionId` (4 new unit tests).
- **Async dispatch + deferral.** `ElsaDispatchHandler` (one per discovered request type) POSTs the
  message to the Elsa `broker-dispatch` workflow and returns `RequestResult(Deferred: true)`;
  `SubmitRequestConsumer` leaves the record `Processing` until a callback finalizes it.
- **Shared-secret callback.** A second Kestrel listener (`:5080`, plain HTTP) hosts
  `POST /internal/requests/{id}/result`, validated by `X-Callback-Secret`, that sets the terminal status.
  The mTLS ingress stays on `:5001`.
- **Docker lab.** `docker-compose` adds the **Elsa 3 Server + Studio** image (multi-arch; runs native
  arm64 — the amd64 image crashes under Rosetta) mounting `workflows/` at `/app/Workflows`. Studio at
  `http://localhost:13000` (`admin`/`password`) is the author-and-export-back feedback loop.
- **Docs.** Rewrote the Elsa integration article (folder convention, async-callback model, Studio loop),
  a full UAT-oriented getting-started, an **Elsa + Broker deployment** article with a PlantUML diagram
  (rendered offline), and [ADR-0006](docs/adr/0006-elsa-workflows-as-the-processing-model.md).
- **Next phase (planned):** a `pivlib`-backed Elsa activity so workflows can interrogate PKI/PIV file
  types delivered as Base64 in messages.

### Added
- Solution-level `Directory.Build.props` enabling `ImplicitUsings`, `Nullable`, `LangVersion=latest`,
  and `GenerateDocumentationFile` (XML docs for DocFX) across all projects.
- `BrokerDbContextFactory` (`IDesignTimeDbContextFactory`) so `dotnet ef migrations` works without
  booting a service host.
- **EF Core `InitialCreate` migration** — domain tables (`RequestRecords`, `RequestTypeDefinitions`)
  plus the MassTransit `InboxState` / `OutboxState` / `OutboxMessage` tables.
- **`ElsaBroker.Tests`** xUnit project: `RequestTypeRegistry` tests (case-insensitivity, DB-over-JSON
  override, inactive-row handling) and `SubmitRequestConsumer` behaviour tests via the MassTransit
  in-memory test harness + EF Core in-memory provider (completed / faulted / missing-record / throwing
  handler / redelivery). Coverage via Coverlet (Cobertura).
- `.claude/agents/` team: `dotnet-architect`, `db-architect`, `security`, `test-lead`, `docs-web`.
- Project meta files: `.gitignore`, `CLAUDE.md`, `CHANGELOG.md`, root `README.md`.
- **DocFX documentation site** (`docs/` + `src/web/`): conceptual articles (introduction, getting
  started, architecture, messaging design, security model), generated C# API reference, an **ADR log**
  using the MADR template (0001–0004 + template), and a **Test Results** section embedding the xUnit
  HTML report and ReportGenerator coverage report.
- **Architecture diagram pipeline**: `docs/diagrams/broker.netjson` → PlantUML (via the sibling
  `netjson-diagrams` CLI) → SVG, rendered fully offline (PlantUML MIT jar, pinned + SHA-256 verified,
  Smetana layout, no Graphviz/network). `docs/scripts/render-diagram.sh` regenerates it.
- **Elsa integration guide** (`docs/articles/elsa-integration.md`): how to use an Elsa workflow as a
  Processor `IRequestHandler` (embedded `IWorkflowRuntime` and remote-server variants), with an example
  invoice workflow (`docs/elsa/invoice-process.elsa.json`) and a Mermaid diagram generated from it by the
  sibling `elsa-to-mermaid` CLI (rendered by DocFX's bundled Mermaid). `render-diagram.sh` regenerates it.
- **Build wiring**: `src/web/build.sh` runs the whole pipeline (diagram → reports → DocFX) into
  `src/web/dist`; the `com.ariugwu` Makefile gains a `build-broker` target that builds it and copies to
  `dist/elsa-broker` (part of the default `make build`).

### Fixed
- **Build did not compile as scaffolded.** Resolved five blocking issues:
  1. `net9.0` projects with no .NET 9 SDK present (installed 9.0.314 to `~/.dotnet`).
  2. `ElsaBroker.CertTools` / `ElsaBroker.Contracts` referenced `Path`/`File`/`Guid`/`DateTime` with no
     usings — fixed via `ImplicitUsings`.
  3. `ElsaBroker.Queue` declared `requestTypes.json` as a duplicate `Content` item (`Include` → `Update`).
  4. Both services used **non-existent MassTransit APIs** (`mt.UsingMsSql(...)`, `sql.Host(connString)`).
     Rewritten to `UsingSqlServer` + `SqlTransportOptions` + `AddSqlServerMigrationHostedService()`.
  5. `ElsaBroker.Processor` (Worker SDK) was missing the `Microsoft.Extensions.Hosting` package, so
     `Host.CreateApplicationBuilder` would not resolve.
- **Live end-to-end run surfaced four more bugs** (found bringing up the real mTLS path against SQL
  Server 2022); all fixed:
  6. `ElsaBroker.CertTools` loaded the CA PFX with `EphemeralKeySet`, which throws on macOS
     (`PlatformNotSupportedException`) — removed the flag.
  7. Generated certs omitted the **Authority Key Identifier** extension, so OpenSSL 3 / Python rejected
     the chain — the cert tool now adds AKI from the issuing CA.
  8. Kestrel set `ClientCertificateMode.RequireCertificate` but supplied **no `ClientCertificateValidation`
     callback**, so client certs signed by the internal CA were rejected at the TLS layer before
     `MtlsAuthHandler` ran. Now defers validation to the app (chain + allowlist).
  9. `MtlsAuthHandler.FindCaCertificate` opened the `LocalMachine\CA` store (unsupported on macOS) and
     threw instead of falling back to the on-disk `ca.crt`. Now guarded.

### Changed
- `docker-compose.yml` now uses the real **SQL Server 2022** image pinned to `linux/amd64` (run via
  Docker's Rosetta emulation on Apple Silicon), `MSSQL_SA_PASSWORD`, and a `sqlcmd` healthcheck. Azure
  SQL Edge was evaluated and rejected (retired, not production-faithful). See
  [ADR-0005](docs/adr/0005-sql-server-on-apple-silicon-via-rosetta.md).

### Validated
- **Full end-to-end run on 2026-06-06** against SQL Server 2022 (Rosetta): EF `InitialCreate` applied,
  MassTransit SQL transport provisioned and `Bus started`, and a request submitted over mutual TLS as
  `ClientA` flowed `Queued → Completed` (Processor handler result recorded; `RequestRecords` audit row
  `ClientA / InvoiceProcess / Completed`). Driver: `docs/scripts/smoke-client.py`.
