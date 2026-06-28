# Backlog

Groomed, prioritized, **not-yet-scheduled** work. The PM pulls from the top into a sprint. Items carry a
stable `EB-###` id, a proposed owner + size (refined at grooming), and acceptance criteria. Status:
`Raw` (just captured) → `Groomed` (ready to pull).

> Seeded 2026-06-08 from the founding prompt. The PM should groom these before proposing the first
> sprint, and confirm sizes/owners.

## Recorded decisions (apply across the backlog)

- **Versioning intent (approved 2026-06-08, sprint v0.1.0):** v0.1.0/MINOR was chosen freely as the
  first tagged release — nothing external consumes the project yet. Going forward: use **pre-release
  tags** (e.g. `-beta`) to mark in-progress / not-yet-stable increments, and **reserve a clean `1.0.0`
  for the public NuGet-package launch** (the natural endpoint of `EB-004`'s packaging arc — that's the
  point at which the public contract needs to harden and SemVer starts mattering to consumers).
- **`EB-001` gates `EB-002` / `EB-003` / `EB-004` (approved 2026-06-08, sprint v0.1.0):** none of these
  three may be pulled into a future sprint until the `EB-001` ADR lands **and** records an explicit
  recommendation (keep / adjust / adopt Elsa's built-in dispatch). Rationale: `EB-002` builds directly
  on the dispatch topology the ADR evaluates; `EB-003` depends on `EB-002`; `EB-004`'s package-boundary
  decision is informed by whichever topology the ADR recommends keeping. Pulling any of them earlier
  risks building on an architecture the ADR may recommend changing. The PM checks this gate before
  proposing `EB-002`/`EB-003`/`EB-004` for any sprint.
- **Gate cleared (2026-06-08, sprint v0.1.0 close):** `EB-001` shipped as ADR-0007 — custom topology
  confirmed. `EB-002` and `EB-004` are now unblocked and pullable. `EB-003` remains blocked on `EB-002`.

---

## EB-001 · ADR: roll-our-own MassTransit topology vs Elsa's built-in dispatch
**Owner (proposed):** dotnet-architect (+ docs-web) · **Size:** M · **Status:** Done (v0.1.0) · **Priority:** 1

We built our **own MassTransit SQL-transport topology** (Queue API + outbox + Processor) and dispatch to
Elsa over **HTTP** (the `broker-dispatch` workflow + shared-secret callback), rather than leaning on
Elsa's own MassTransit integration / built-in workflow dispatch. The architect should evaluate whether
our approach is the right long-term bet.

Enumerate **our needs** the comparison must weigh:
- **Dynamic request types** — added by dropping a workflow in `workflows/`, no redeploy.
- **Dynamic workflow loading** shareable for configuration — the same folder feeds Elsa (import) and the
  broker (`requestType → definitionId`), carrying shared config (`requestType`, workflow definition id,
  workflow instance / correlation id, callback contract).
- mTLS ingress + audit trail + transactional outbox owned by the broker, independent of Elsa's runtime.
- Async callback (decoupled from `WaitForCompletion` reliability — see why we left the demo image).

**Acceptance criteria**
- [ ] New ADR (next number, `0008`+) in `docs/adr/`, wired into the ADR index + toc.
- [ ] A **SWOT quadrant chart** comparing the two approaches, as a Mermaid `quadrantChart` (DocFX renders
      Mermaid; if the bundled version doesn't support `quadrantChart`, fall back to a rendered SVG or a
      2×2 markdown table — don't ship a broken diagram).
- [ ] Explicit "needs" table mapping each need → how each approach satisfies it.
- [ ] Architect's recommendation (keep / adjust / adopt Elsa's) with rationale.
- [ ] References the Elsa HTTP-workflows tutorial: <https://docs.elsaworkflows.io/guides/http-workflows/tutorial>

---

## EB-002 · Structured logging + pluggable `ElsaBroker.ILogShipper` (ELK/Splunk)
**Owner (proposed):** dotnet-architect (+ docs-web for Docker) · **Size:** L · **Status:** Done (v0.2.0) · **Priority:** 1

> **Done (2026-06-08, sprint v0.2.0):** `ILogShipper` abstraction, `BrokerLogEvent`, four shipper
> implementations (`NullLogShipper`, `ConsoleLogShipper`, `HttpJsonLogShipper`, `CompositeLogShipper`),
> DI helpers, service wiring (Queue: 2 events; Processor: 5 lifecycle points), Seq compose profile,
> and structured-logging docs article all shipped. 16/16 tests pass. EB-003 is now unblocked.

Log locally **and** ship structured logs to external sinks (ELK, Splunk, OpenSearch, …) behind a modular
interface anyone can implement: **`ElsaBroker.ILogShipper`**. Make the sink a Docker-compose option.

**Acceptance criteria**
- [ ] `ElsaBroker.ILogShipper` abstraction (structured event in → sink out) + DI registration; a no-op
      and at least one real implementation (e.g. Elasticsearch/OpenSearch or a generic HTTP/JSON sink).
- [ ] Structured logging (Serilog or `Microsoft.Extensions.Logging` + structured enrichers) across Queue
      + Processor + WorkflowServer, correlated by `CorrelationId`.
- [ ] A compose profile that stands up a sink (e.g. OpenSearch + Dashboards) the shipper targets.
- [ ] Docs: how to add a custom shipper; how to enable the stack.

---

## EB-003 · DIY local message dashboard (Blazor) — a free ELK/Splunk stand-in
**Owner (proposed):** frontend-dev (+ docs-web) · **Size:** L · **Status:** Done (v0.4.0) · **Priority:** 1

> **Done (2026-06-08, sprint v0.4.0):** Switchboard dashboard shipped — Blazor Server + Fluent UI
> v4, `SwitchboardLogShipper` (SQLite), `POST /api/events` HTTP ingestion, Overview/Events/Faults
> pages with seed data, Dockerfile, and docker-compose `"dashboard"` profile service on port 5002.
> 16/16 tests pass. See [release/v0.4.0.md](../release/v0.4.0.md).
>
> **Fully unblocked (2026-06-08, sprint v0.3.0 close):** Both blocking items cleared — EB-002
> shipped `ILogShipper` (v0.2.0) and EB-004 settled the packaging boundary (v0.3.0).
> **Owner resolved (2026-06-08):** `frontend-dev` agent added to `.claude/agents/` per user
> direction. Blazor Server specialist. Ready to pull into v0.4.0.

Built on `ILogShipper` (EB-002): a **Blazor Server** dashboard showing messages **submitted /
processing / dispatched / completed / faulted**, throughput, and per-request-type breakdowns — a free,
local stand-in when there's no ELK/Splunk.

**Name (team to pick — PM lead candidate first):** **Switchboard** · Semaphore · Lookout · Pigeonhole ·
Telegraph. (Evokes routing/visibility of messages through the broker.)

**Design decisions (approved 2026-06-08):**

- **SQLite persistence.** Events are stored in a SQLite database on the filesystem. The DB is created
  automatically if it doesn't exist. Location is configurable via `Switchboard:DatabasePath` in
  appsettings (default: `./switchboard.db`), overridable at runtime via environment variable.
- **Blazor Fluent UI framework** ([fluentui-blazor.net](https://www.fluentui-blazor.net/)). Use the
  `Microsoft.FluentUI.AspNetCore.Components` NuGet package for all UI components — data grids, cards,
  charts, navigation, theming.
- **Visual design:** heavily inspired by **Azure Portal** screens (resource overview blades, metric
  cards, activity logs) and **Kibana** data browsing/analysis features (filterable event streams,
  time-range selectors, aggregation charts, drill-down from summary to detail).

**Acceptance criteria**
- [ ] Name chosen + recorded (ADR or release note).
- [ ] Blazor Server app using **Fluent UI** components (`Microsoft.FluentUI.AspNetCore.Components`).
- [ ] **SQLite `ILogShipper` sink** — `SwitchboardLogShipper` implements `ILogShipper`, writes
      `BrokerLogEvent` records to a SQLite database via EF Core SQLite. DB auto-created on first run;
      path configurable via `Switchboard:DatabasePath` (default `./switchboard.db`).
- [ ] **Azure Portal-inspired overview:** metric cards (total submitted, processing, completed, faulted),
      status funnel visualization, throughput over time chart.
- [ ] **Kibana-inspired event browser:** filterable/searchable event stream (by EventType, Level,
      RequestType, ClientId, CorrelationId), time-range selector, detail drill-down panel.
- [ ] **Per-request-type breakdown:** counts and status distribution by request type.
- [ ] **Recent faults view:** filtered list of faulted events with exception details.
- [ ] Runs locally (and optionally as a compose service); offline, no telemetry, no CDN assets.
- [ ] Depends on **EB-002** (`ILogShipper` / `BrokerLogEvent` from `ElsaBroker.Abstractions`).

> **Owner resolved (2026-06-08):** `frontend-dev` agent added to `.claude/agents/`. Blazor Server +
> Fluent UI specialist.

---

## EB-004 · Package as NuGet + Docker — a sharable "Elsa deployment strategy"
**Owner (proposed):** dotnet-architect (+ docs-web) · **Size:** L · **Status:** Done (v0.3.0) · **Priority:** 2

> **Done (2026-06-08, sprint v0.3.0):** ADR-0008 (three-package Option B), `ElsaBroker.Abstractions`
> project, `dotnet pack` producing 3 `.nupkg` files, Queue + Processor Dockerfiles,
> `docker-compose.reference.yml`, deploy quickstart docs, `.dockerignore` + `Directory.Build.props`
> + solution file fixes all shipped. 16/16 tests pass. See [release/v0.3.0.md](../release/v0.3.0.md).

This is becoming a reusable, opinionated **Elsa deployment strategy** (broker + dispatcher workflow +
custom Elsa server + Docker). Decide whether to **restructure now** for packaging, and how to ship it.

**Acceptance criteria**
- [ ] Decision (ADR) on package boundaries — e.g. `ElsaBroker.Contracts`, `ElsaBroker.Data`,
      `ElsaBroker.Abstractions` (incl. `ILogShipper`), `ElsaBroker.Queue`, `ElsaBroker.Processor`,
      `ElsaBroker.WorkflowServer` — and whether to restructure the repo now vs later.
- [ ] `dotnet pack` producing the chosen packages (versioned per this process); CI notes.
- [ ] A reference Dockerfile / compose users can adopt.
- [ ] Docs: "deploy ElsaBroker" quickstart for consumers.
- [ ] Depends on EB-002 for the `ILogShipper` package boundary.

---

## EB-005 · Containerize the custom Elsa server + Studio (one `docker compose up`)
**Owner (proposed):** docs-web (+ dotnet-architect) · **Size:** M · **Status:** Done (v0.1.0) · **Priority:** 2

The custom `ElsaBroker.WorkflowServer` (folder importer, no MassTransit) is validated **locally**. Make
the lab one command: Dockerfile for the server + compose service, the official **Elsa Studio** image
pointed at it (`ELSASERVER__URL`), and SQL. Replace the retired single demo-image approach.

**Acceptance criteria**
- [ ] Multi-stage net9 Dockerfile for `ElsaBroker.WorkflowServer`; `workflows/` mounted/copied to the
      content root so the importer seeds it.
- [ ] compose: `sqlserver` + `elsa-server` (build) + `elsa-studio` (official image → server); broker
      `Elsa:DispatchUrl`/`CallbackBaseUrl` updated for the container network.
- [ ] `docker compose up` → server imports the folder (defs > 0), Studio connects, e2e passes.

## EB-006 · Docs: rewrite to the custom-server reality
**Owner (proposed):** docs-web · **Size:** M · **Status:** Done (v0.1.0) · **Priority:** 3

Update getting-started / elsa-integration / elsa-deployment to the **custom server** (not the demo
image): the `/workflows/<path>` base, the folder-import-on-startup, the Studio authoring/export loop,
and the gotchas we hit (chunked `PostAsJsonAsync` → use `StringContent`; JS headers need `({…})` parens;
demo image's MassTransit breaks `WaitForCompletion`). Refresh the deployment PlantUML.

**Acceptance criteria**
- [ ] Articles + deployment diagram reflect `ElsaBroker.WorkflowServer`; DocFX builds 0/0.
- [ ] The three integration gotchas captured (ADR or article).

## EB-007 · PIV issuance pipeline — IDMS Emulator plugin with pivlib integration

**Owner (proposed):** dotnet-architect (+ frontend-dev, docs-web) · **Size:** XL · **Status:** Groomed / partially Pulled (v0.5.0) · **Priority:** 2

> **Groomed 2026-06-08 (sprint v0.5.0):** EB-007 decomposed into five sub-cards (EB-007a through
> EB-007e). **EB-007a gates the entire arc** — the same pattern as EB-001 gating EB-002/003/004.
> EB-007a is pulled into v0.5.0. EB-007b through EB-007e are Blocked/Deferred until EB-007a lands and
> the plugin contract is confirmed.

A full **PIV issuance and management pipeline** that showcases ElsaBroker handling real-world HSPD-12 /
FIPS 201-3 workflows. The [`app.pivlib`](https://github.com/ariugwu/app.pivlib) project (Rust + WASM
biometric processing: INCITS 385 facial records, INCITS 381/378 fingerprint image + minutiae records,
CBEFF containers) provides the artifact processing capabilities. The PIV topology is documented in the
[NetJSON PIV issuance graph](../../docs/diagrams/) (Registration Authority → Enrollment Station →
IDMS → CMS → CA → HSM → PACS → logical access → mobile DPC → S/MIME).

**Architecture:**

- **ElsaBroker stays agnostic.** It is the durable, mTLS-secured messaging backbone — it knows nothing
  about PIV, biometrics, or HSPD-12. It processes generic `ISubmitRequest` messages and dispatches them
  to Elsa workflows.
- **`app.pivlib` owns the domain.** All PIV-specific workflows, request types, and biometric processing
  logic live in the pivlib project as part of a new **IDMS Emulator plugin system**. ElsaBroker is the
  first plugin target — pivlib provides the Elsa workflow definitions and request-type configurations
  that the broker loads from its `workflows/` folder.
- **IDMS Emulator** sends typed messages to ElsaBroker simulating a real Identity Management System:
  enrollment submissions, fingerprint capture (WSQ → INCITS 381/378 via pivlib), facial image capture
  (JPEG → INCITS 385 via pivlib), vetting requests (DCSA background check), card issuance requests.
- **Interconnected systems are mocked by Elsa workflows** returning static responses. For example:
  - A DCSA fingerprint submission sends a message from the IDMS → Broker → Elsa workflow → static
    "vetting approved" response.
  - A PACS provisioning request sends FASC-N / Card UUID → Elsa workflow → static "provisioned" ACK.
  - A CMS card personalization request → static "card personalized" response.
  - Each mock system is a separate Elsa workflow in the `workflows/` folder, discoverable by the
    broker's `WorkflowFolderScanner` via `customProperties.requestType`.

**Sub-cards (decomposed 2026-06-08):**

### EB-007a · Plugin system ADR
**Owner:** dotnet-architect (+ docs-web for ADR wiring) · **Size:** M · **Status:** Pulled (v0.5.0) · **Gate:** yes — blocks EB-007b through EB-007e

Define the plugin contract: how pivlib (or any domain project) provides Elsa workflow definitions,
request-type configurations, and processing logic to ElsaBroker without any broker code changes.

**Acceptance criteria**
- [ ] New ADR (next number after ADR-0008) in `docs/adr/`, wired into the ADR index + toc.
- [ ] Plugin contract defined: folder/package convention for supplying workflow JSON definitions,
      `customProperties.requestType` mapping, mTLS client certificate provisioning for the emulator,
      and callback secret distribution — all without modifying broker source.
- [ ] Decision on where the plugin folder lives relative to the broker's `workflows/` mount — options
      include symlink/volume-mount (compose), NuGet content-files copy-on-build, or a CLI import step.
- [ ] Decision on whether ElsaBroker needs any new surface (e.g. a `plugins/` companion folder
      alongside `workflows/`, or a manifest file) or whether the existing `WorkflowFolderScanner`
      convention is sufficient.
- [ ] Architect's recommendation with rationale; any constraints on pivlib's side noted.
- [ ] ADR references the `workflows/` folder-scanner convention (CLAUDE.md / ADR-0006) and the
      packaging boundary (ADR-0008) as inputs.

### EB-007b · Reference plugin example
**Owner:** dotnet-architect · **Size:** M · **Status:** Done (v0.5.0)

> **Done (2026-06-09, sprint v0.5.0):** `examples/pivlib-plugin/` shipped — 4 mock PIV
> workflows (PivEnroll, DcsaVetting, CmsPersonalization, PacsProvisioning), request-type
> config templates, allowlist template, Docker Compose overlay, mTLS smoke test client,
> plugin authoring README, and scanner test validating all 4 request types are discovered.
> Scope reworked from "IDMS Emulator core" to "reference plugin example" — the broker team
> owns the platform, not the domain; the IDMS Emulator is pivlib's deliverable.

A working reference plugin in `examples/pivlib-plugin/` that validates the ADR-0009 plugin
contract end-to-end and serves as the "plugin starter kit" for external teams.

**Acceptance criteria**
- [x] Plugin directory with workflow JSONs, config templates, compose overlay, test client, README.
- [x] `WorkflowFolderScanner` discovers all 4 PIV request types from the plugin directory.
- [x] Scanner test validates plugin workflows are well-formed and discoverable.
- [x] Plugin authoring guide clear enough for an external team to follow.

### EB-007c · Mock Elsa workflows (DCSA, CMS, PACS)
**Owner:** dotnet-architect · **Size:** — · **Status:** Done (folded into EB-007b, v0.5.0)

> **Folded (2026-06-09):** The 4 mock PIV workflows are delivered as part of EB-007b
> (reference plugin example). No separate deliverable needed.

### EB-007d · pivlib biometric integration
**Owner:** cross-team (pivlib + broker) · **Size:** L · **Status:** Deferred (cross-team)

> **Deferred (2026-06-09):** Requires pivlib team collaboration to wire WASM modules
> (WSQ decode, INCITS 381/378/385 encoding, CBEFF wrapping) into Elsa activities. The
> broker team provides the platform (`examples/pivlib-plugin/` as starter kit); the pivlib
> team owns the domain integration. Gate: EB-007a (done) + pivlib team availability.

Wire pivlib's WASM modules into the IDMS Emulator pipeline so at least one flow processes
a real biometric artifact.

**Acceptance criteria**
- [ ] At least one end-to-end biometric flow: submit a WSQ fingerprint file → pivlib extracts minutiae
      (INCITS 378) + encodes finger image record (INCITS 381) → CBEFF-wrapped result submitted to the
      broker → Elsa "authority analysis" workflow returns static approval.
- [ ] pivlib WASM modules invoked from the emulator (not stubbed); real WSQ file used in the demo.

### EB-007e · End-to-end PIV demo compose
**Owner:** cross-team (pivlib + broker) · **Size:** M · **Status:** Deferred (cross-team)

> **Deferred (2026-06-09):** Requires pivlib team to build IDMS Emulator + biometric
> pipeline (EB-007d). The broker team provides the compose overlay pattern
> (`examples/pivlib-plugin/docker-compose.pivlib.yml`); the full demo is a cross-team
> deliverable.

`docker compose up` brings up the full PIV issuance pipeline and runs a complete lifecycle from
enrollment to card activation.

**Acceptance criteria**
- [ ] `docker compose up` (one command) starts: SQL Server, ElsaBroker Queue + Processor, Elsa Server,
      Switchboard dashboard, IDMS Emulator.
- [ ] IDMS Emulator runs the full PIV lifecycle automatically on startup (or via a `--demo` flag) and
      exits 0.
- [ ] Switchboard shows all lifecycle events post-run.
- [ ] Docs: "PIV issuance demo" article in `docs/articles/`.

**Gate decisions (recorded 2026-06-08, sprint v0.5.0):**

- **EB-007a gates EB-007b through EB-007e.** None may be pulled into a sprint until EB-007a lands and
  the plugin contract is explicitly confirmed. Rationale: the emulator architecture, mock workflow folder
  convention, pivlib integration approach, and compose topology all depend on the contract the ADR
  establishes. Pulling any implementation card earlier risks building on an architecture the ADR may
  change.

**Depends on:** EB-002 (ILogShipper — Done v0.2.0), EB-003 (Switchboard — Done v0.4.0), EB-004 (packaging — Done v0.3.0)

**Reference files:**

- pivlib project: `/Users/ariugwu/Projects/app.pivlib/` (Rust + WASM biometric processing)
- PIV issuance topology (NetJSON): `netjson-diagrams/tests/fixtures/network_graph_piv_issuance.json`
- PIV topology nodes: RA, Enrollment Station, IDMS, CMS, CA, HSM, LDAP, OCSP, Card Personalization,
  PACS (head-end, panel, reader), AD (PKINIT), DPC Issuer, MDM, Mobile, Mail (S/MIME)

---

## ✅ EB-008 · DocFX site → match the Ink theme colors (portfolio web standard) — DONE (2026-06-11)
**Owner:** docs-web · **Size:** M · **Status:** Done (2026-06-11) · **Priority:** 3 · **Serves:** EPIC-011

> **Done 2026-06-11:** added `docs/templates/ink/public/main.css` (Bootstrap-variable overrides → Tufte
> paper/ink base + Ink accents; `#3a3a3a` navbar/footer; brass links) and wired `"templates/ink"` into the
> `template` array in `docfx.json`. **Verified:** `docfx build` succeeds **0 warnings / 0 errors** and the
> Ink `main.css` is emitted to `src/web/dist/public/main.css`. (Build needs the .NET 9 runtime —
> `DOTNET_ROOT=$HOME/.dotnet`.)


The portfolio standardized on the **Ink** scheme + Tufte base as the unified web look
(EPIC-011). The public face of
ElsaBroker is a **DocFX static site** (`dist/elsa-broker`, served at `https://ariugwu.com/elsa-broker`), so the React
two-column layout can't be reused — instead, **match the Ink colors** via DocFX theming/custom CSS (paper/ink base,
Ink accents `#3a3a3a`/`#5a5a5a`/`#8a6d3b`/`#11120f`). Keep assets local (no CDN), trust-report posture intact.

**Acceptance criteria**
- [ ] DocFX template/custom CSS applies the Ink palette (paper/ink + accents) to the site chrome + content.
- [ ] Fonts/assets self-hosted (no CDN/telemetry); `docfx build` clean; the site visibly matches the portfolio standard.

## EB-009 · Host the DocFX site as a GitHub Pages site (blocked on secrets/CI — EPIC-010)
**Owner (proposed):** release-manager (+ docs-web) · **Size:** M · **Status:** Blocked · **Priority:** 3 · **Serves:** Career Showcase

Owner ask (2026-06-11): eventually host the DocFX `dist/elsa-broker` site in a **GitHub repo as a GitHub Pages
site**. This is grouped with the project's **auth / pipeline / package-publishing credential** items: GitHub Pages
deploy + the NuGet publish of `EB-004`'s packages both need **publish credentials and a pipeline**, which the
portfolio has **deferred** until a secrets strategy lands
(EPIC-010; recorded CSO decision,
2026-06-10 — no pipelines/secrets in any repo until then). **Do not pull until EPIC-010 lands.** Until then, builds
are manual + checklist-gated.

**Acceptance criteria**
- [ ] (Gated by EPIC-010) GitHub Pages deploy of `dist/elsa-broker` from a pipeline, secrets-safe (no token in repo/workflow).
- [ ] NuGet publish of the `EB-004` packages wired into the same secrets-safe flow.
- [ ] Deploy checklist's "Secrets / CI" section flips from deferred to the real flow for this project.

---

## Deferred / parked

_(Items captured from prompts but intentionally not scheduled — keep the team focused. Add here with a
one-line rationale.)_

- **EB-009 (GitHub Pages + NuGet publish)** is parked behind **EPIC-010** (secrets strategy) per the CSO decision —
  listed above as `Blocked`, not pullable until secrets/CI is solved portfolio-wide.
