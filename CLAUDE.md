# CLAUDE.md — ElsaBroker

## Project

ElsaBroker is a **durable, mTLS-secured queueing front end for Elsa 3 workflows** (renamed from
msg-broker on 2026-06-07). Clients submit typed requests over an mTLS API; the broker authorizes them,
delivers them durably (MassTransit SQL transport + EF outbox), and **dispatches each request type to an
Elsa 3 workflow** for the actual work — there are no hand-written C# handlers. It is **resilient**
(transactional outbox/inbox, async callbacks) and **safe by default** (authorization asserted by the
client certificate, never the caller).

> The .NET solution lives at `src/services/ElsaBroker/`. The public face of the project is a **DocFX
> site** that ships to `dist/elsa-broker` (served at `https://ariugwu.com/elsa-broker`).

## Elsa integration (the processing model)

- **Shared `workflows/` folder** (`src/services/ElsaBroker/workflows/`) is the source of truth: the Elsa
  server mounts it at `/app/Workflows`; the Processor scans it (`WorkflowFolderScanner` →
  `WorkflowRegistry`) to map `requestType → definitionId`. Convention: each request-handling workflow
  declares `customProperties.requestType`.
- **Async dispatch:** `ElsaDispatchHandler` (one per request type) POSTs to the Elsa `broker-dispatch`
  workflow and returns `Deferred`; `SubmitRequestConsumer` leaves the record `Processing`. The workflow
  finalizes via `POST /internal/requests/{id}/result` on the Queue's **:5080** listener
  (shared secret `Elsa:CallbackSecret`, must match in Queue + Processor + the workflow's header).
- **Lab:** `docker compose up -d` → SQL Server + **Elsa Server+Studio** (`http://localhost:13000`,
  `admin`/`password`). Author/refine workflows in Studio, export JSON back into `workflows/`. Run the
  broker locally (`ElsaBroker.Queue` on :5001/:5080, `ElsaBroker.Processor`). See
  [ADR-0006](docs/adr/0006-elsa-workflows-as-the-processing-model.md) and the getting-started doc.

## Tech stack

| Layer | Technology |
|-------|-----------|
| Runtime | **.NET 9** (`net9.0`) |
| Messaging | **MassTransit 8.3** on the **SQL Server transport** (`UsingSqlServer`) |
| Persistence | **EF Core 9** + SQL Server; MassTransit EF **outbox/inbox** |
| Transport security | **mTLS** — Kestrel `RequireCertificate`, internal CA, client allowlist |
| Docs site | **DocFX** (API ref + articles + ADRs + test results) → `dist/elsa-broker` |

## ⚠️ Toolchain note — the SDK is NOT system-wide

The system `dotnet` is **8.0** and cannot target `net9.0`. The .NET 9 SDK + the `dotnet-ef` tool are
installed under `~/.dotnet`. **Prefix every dotnet command with the right PATH:**

```bash
export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"
cd src/services/ElsaBroker
```

## Build / test / migrate commands

```bash
dotnet build                                              # whole solution (0 warnings expected)
dotnet test --collect:"XPlat Code Coverage" \
            --results-directory reports/coverage          # xUnit + Coverlet (Cobertura)
dotnet ef migrations add <Name> --project ElsaBroker.Data  # uses BrokerDbContextFactory (no host boot)
docker compose up -d                                      # SQL Server for local run
```

`Directory.Build.props` enables `ImplicitUsings`, `Nullable`, and `GenerateDocumentationFile` for every
project, so individual csproj files stay minimal. XML docs (for DocFX) are generated on build.

## Project layout (`src/services/ElsaBroker/`)

```
ElsaBroker.Contracts     ISubmitRequest envelope + RequestStatus — stable, additive-only
ElsaBroker.Data          BrokerDbContext, entities, registry, Migrations/, design-time factory
ElsaBroker.Queue         ASP.NET Core API — mTLS auth, validate, enqueue, poll  (Web SDK)
ElsaBroker.Processor     Worker — consume, dispatch to IRequestHandler, audit   (Worker SDK)
ElsaBroker.CertTools     CLI — CA + server/client cert generation
ElsaBroker.Tests         xUnit — registry + consumer (MassTransit harness + EF InMemory)
docs/                   DocFX site sources (articles, ADRs, NetJSON example, test results)
```

## Messaging design (own this in `dotnet-architect`)

- **Bus factory:** `mt.UsingSqlServer(...)`; connection via `SqlTransportOptions.ConnectionString`;
  `AddSqlServerMigrationHostedService()` provisions the transport schema on startup. (The scaffold's
  original `UsingMsSql` / `sql.Host(connStr)` were **not real APIs** — see CHANGELOG.)
- **Outbox/inbox:** `AddEntityFrameworkOutbox<BrokerDbContext>()` with `UseBusOutbox()`. Status writes
  and publishes are atomic with the DB transaction — never publish outside the outbox.
- **Idempotency:** consumers key on `CorrelationId` and must tolerate redelivery. The audit record is
  the source of truth; redelivery re-runs the handler but converges on the same terminal state.
- **Status lifecycle:** `Queued → Processing → Completed | Faulted` on `RequestRecord`.

## Security model (own this in `security`)

- Kestrel requires a client cert; `MtlsAuthHandler` checks **CA chain + validity window + allowlist
  thumbprint**. `ClientId` is taken from the **cert claim**, never the request body. A client may only
  poll its own records. Registry authorizes `(ClientId, RequestType)`.
- `certs/`, `*.pfx`, keys are git-ignored. Rotation = issue + update allowlist; revocation = remove from
  allowlist.

## The team (`.claude/agents/`)

`project-manager` (agile process — sprints/kanban/releases/backlog/SemVer), `dotnet-architect`
(messaging/resiliency/scale), `db-architect` (EF Core/migrations/schema), `security` (mTLS/supply-chain),
`test-lead` (xUnit/harness/coverage), `docs-web` (DocFX/ADRs/diagrams/landing), `frontend-dev`
(Blazor Server UI/Switchboard dashboard).

## Agile process (`docs/agile/`)

The PM runs a lightweight process: a **sprint = one Claude session**, **SemVer per shippable request**
(PM proposes the bump, architect confirms MAJOR), a **kanban per version** in `docs/agile/sprints/`,
**release reports** in `docs/agile/release/` that feed the `CHANGELOG.md`, and a groomed
`docs/agile/backlog/`. The usage limit can't be detected programmatically, so the board is **checkpointed
after every task** to survive a mid-sprint cutoff. See [docs/agile/README.md](docs/agile/README.md).
`docs/agile/**` is excluded from the public DocFX site (internal process).

## Docs & reuse

- The architecture diagram is **generated**, not drawn: a NetJSON document in `docs/` is piped through
  the sibling `software/netjson-diagrams` CLI (`netjson-diagrams <file> -o <out.puml>`) to produce
  PlantUML. Render offline — never call plantuml.com/kroki.io (matches the no-telemetry stance).
- ADRs use the **MADR** template under `docs/adr/`; they render as a DocFX section.
- Coverage/test HTML (ReportGenerator) feeds the DocFX "Test Results" section. `reports/` is git-ignored
  and regenerated by the build.

## Critical gotchas

1. **Use `~/.dotnet/dotnet`** — the system SDK is 8.0 and will fail with `NETSDK1045`.
2. **Worker SDK does not auto-reference `Microsoft.Extensions.Hosting`** — `ElsaBroker.Processor`
   references it explicitly. Don't remove it.
3. Migrations are **append-only**; `InitialCreate` already includes the inbox/outbox tables.
4. **Validated end-to-end** (2026-06-06) against SQL Server 2022: mTLS submit → outbox → SQL transport →
   Processor → audit `Completed`. Reproduce with `docs/scripts/smoke-client.py`.
5. **Apple Silicon:** the SQL Server image is amd64 and needs Docker Desktop's **Rosetta** emulation
   (Settings ▸ General ▸ "Use Rosetta…"); under plain QEMU `sqlservr` crashes. `docker-compose.yml`
   pins `platform: linux/amd64`. See [ADR-0005](docs/adr/0005-sql-server-on-apple-silicon-via-rosetta.md).
6. **Local mTLS run:** generate certs into `ElsaBroker.Queue/certs/` (cert tool, run from that dir), set
   `Mtls:CaThumbprint` in appsettings, add the client thumbprint to `ClientAllowlist.json`, and copy
   `ca.crt` into `bin/.../certs/` (chain lookup is `AppContext.BaseDirectory`-relative). Kestrel defers
   client-cert validation to `MtlsAuthHandler` (chain + allowlist).
7. **Elsa image: run native arm64.** The `elsa-server-and-studio` image is multi-arch; do **not** pin
   `platform: linux/amd64` for it — the amd64 image crashes under Rosetta's JIT
   (`BuilderBase.h block_for_offset`). Only SQL Server needs the amd64/Rosetta pin.
8. **Elsa-only processing:** the legacy C# handler classes (`InvoiceProcessHandler`, etc.) still compile
   but are **not registered** — request types are handled by Elsa workflows discovered from `workflows/`.
