---
status: "accepted"
date: 2026-06-08
decision-makers: dotnet-architect
consulted: docs-web, security
informed: team
---

# Custom MassTransit topology vs Elsa's built-in dispatch (EB-001)

## Context and Problem Statement

ElsaBroker uses a **custom MassTransit SQL-transport topology** to move requests from ingress to
execution: the Queue API validates, audits, and publishes via the EF outbox; the Processor consumes and
dispatches to Elsa 3 over HTTP (the `broker-dispatch` workflow); Elsa calls back when complete. This
topology is operational and validated end-to-end ([ADR-0006](0006-elsa-workflows-as-the-processing-model.md)).

Elsa 3 ships its own MassTransit integration (`Elsa.MassTransit`), which provides workflow activities for
sending/receiving MassTransit messages, a pluggable `IWorkflowDispatcher` that can route workflow
execution through MassTransit (including the SQL transport), and automatic trigger registration from
message types. Should we keep our custom topology, adopt Elsa's built-in dispatch, or pursue a hybrid?

This decision gates EB-002 (structured logging), EB-003 (dashboard), and EB-004 (NuGet packaging) -- all
three build on whichever dispatch topology we commit to.

## Decision Drivers

* **Dynamic request types** -- add a type by dropping a workflow JSON in `workflows/`, no redeploy.
* **Shared workflow folder** -- the same `workflows/` directory feeds both Elsa (import) and the broker
  (`requestType -> definitionId` via `WorkflowFolderScanner`), carrying shared config.
* **mTLS ingress + audit trail + transactional outbox** owned by the broker, independent of Elsa's
  runtime lifecycle.
* **Async callback** decoupled from `WaitForCompletion` reliability -- the reason we moved away from the
  demo image's built-in MassTransit (see [ADR-0006](0006-elsa-workflows-as-the-processing-model.md)).

## Considered Options

* **Option A -- Keep our custom MassTransit topology** (Queue + Processor + HTTP dispatch to Elsa)
* **Option B -- Adopt Elsa's built-in MassTransit dispatch** (collapse Queue + Processor into Elsa's
  `IServiceBus` / `IWorkflowDispatcher`)
* **Option C -- Hybrid: keep the Queue, replace the Processor with Elsa's dispatcher** (partial adoption)

## Decision Outcome

Chosen option: **Option A -- keep our custom MassTransit topology**, because it preserves the clear
responsibility boundary (Queue owns mTLS/audit/SLA, Processor owns routing/durability, Elsa owns
execution), keeps the async callback resilience we deliberately chose, and leaves the package boundaries
clean for EB-004.

### Rationale

1. **Responsibility boundary is clearer.** Queue owns mTLS, audit, and SLA enforcement. Processor owns
   message routing and durability semantics. Elsa owns workflow execution. Collapsing into Elsa's
   built-in dispatch leaks execution concerns (Elsa's process lifecycle, its MassTransit configuration)
   into the routing and audit layers. Each service can be versioned, scaled, and restarted independently.

2. **Async callback is more resilient.** Our callback model (`broker-dispatch` workflow POSTs result to
   Queue `:5080`) lets Elsa fail, restart, or scale independently -- the callback is a plain HTTP POST
   with a shared secret, fire-and-forget from Elsa's perspective. Elsa's `WaitForCompletion` couples the
   workflow duration to the requesting service's connection lifetime; long-running or human-in-the-loop
   workflows would time out or require polling that duplicates what we already have.

3. **Stable request-response contract.** `ISubmitRequest` is the broker's public surface -- additive-only,
   versioned, decoupled from workflow internals. Clients submit and poll against the broker; they never
   interact with Elsa directly. Adopting Elsa's built-in dispatch would push Elsa's message types
   (`DispatchWorkflowRequest`, etc.) into the contract surface, coupling clients to Elsa's internal
   schema evolution.

4. **Packaging arc (EB-004) is cleaner.** Separate packages (`ElsaBroker.Queue`, `ElsaBroker.Processor`,
   `ElsaBroker.Contracts`) let consumers use the Queue API without Elsa, or run the Processor with
   custom handlers for non-Elsa scenarios. Collapsing into Elsa's dispatcher makes the Elsa dependency
   mandatory across the entire stack.

5. **Transport-agnostic future.** Our topology works with any MassTransit transport -- SQL Server today,
   RabbitMQ or Azure Service Bus tomorrow -- because the bus configuration is isolated in each service's
   `Program.cs`. Elsa's built-in dispatch ties to `IServiceBus` and `IWorkflowDispatcher`, whose SQL
   transport support is less documented and tested than the RabbitMQ/ASB paths.

### Consequences

* Good, because architectural stability is confirmed -- EB-002, EB-003, and EB-004 can proceed on the
  current topology without risk of rework.
* Good, because the clear package boundaries are preserved for the EB-004 NuGet packaging arc.
* Good, because the async callback resilience model is validated as the correct long-term bet (decoupled
  from Elsa's runtime lifecycle and `WaitForCompletion` fragility).
* Bad, because operational complexity remains: two services (Queue + Processor) plus the HTTP callback
  hop to Elsa. This is an accepted cost -- the separation buys independence and resilience.
* Neutral, because Elsa's MassTransit integration may mature. If it resolves the known issues (trigger
  reliability, CorrelationId handling, SQL transport documentation), this decision can be revisited via
  a superseding ADR.

### Confirmation

The decision is confirmed by:

- The existing end-to-end validation ([ADR-0006](0006-elsa-workflows-as-the-processing-model.md),
  `docs/scripts/smoke-client.py`) -- submit over mTLS, outbox publish, Processor consume, Elsa dispatch,
  callback, status `Completed`.
- Verifying that EB-002 (structured logging), EB-003 (dashboard), and EB-004 (packaging) implementation
  proceeds cleanly on the current topology without requiring topology changes.

## Pros and Cons of the Options

### Option A -- Keep our custom MassTransit topology

The current architecture: Queue API (mTLS + audit + outbox) -> SQL transport -> Processor
(`SubmitRequestConsumer` -> `ElsaDispatchHandler`) -> HTTP POST to Elsa -> async callback to Queue.

* Good, because mTLS ingress, audit trail (`RequestRecord`), and transactional outbox are first-class,
  owned by the broker.
* Good, because the responsibility boundary is explicit: each service has a single job, can be versioned
  and scaled independently.
* Good, because the async callback decouples workflow duration from the requesting service's lifetime.
* Good, because the contract surface (`ISubmitRequest`) is stable, additive-only, and Elsa-agnostic.
* Good, because transport-agnostic -- switch from SQL to RabbitMQ without touching Elsa.
* Bad, because two extra services to operate (Queue + Processor) compared to a single Elsa deployment.
* Bad, because the HTTP callback adds a network hop and requires a shared secret (weaker than mTLS, but
  internal-only -- see [ADR-0006](0006-elsa-workflows-as-the-processing-model.md)).
* Bad, because the custom dispatcher code (`ElsaDispatchHandler`, `WorkflowFolderScanner`,
  `WorkflowRegistry`) must be maintained.

### Option B -- Adopt Elsa's built-in MassTransit dispatch

Collapse Queue + Processor into Elsa's `IServiceBus` / `IWorkflowDispatcher`. Clients submit messages
that Elsa consumes directly via `ReceiveMassTransitMessage` triggers or `DispatchWorkflowRequestConsumer`.

* Good, because less code -- no `SubmitRequestConsumer`, `ElsaDispatchHandler`, or custom dispatch layer.
* Good, because native Elsa workflow activities (`ReceiveMassTransitMessage`, `SendMassTransitMessage`)
  integrate directly.
* Good, because Elsa manages workflow discovery and dispatch internally.
* Good, because a single deployment unit (Elsa server) reduces operational surface.
* Bad, because no built-in audit trail -- `RequestRecord` lifecycle (`Queued -> Processing -> Completed`)
  would need to be reimplemented as Elsa middleware or a custom activity.
* Bad, because mTLS ingress requires wrapping Elsa's Kestrel with custom middleware (the broker's
  `MtlsAuthHandler` + allowlist model does not apply directly).
* Bad, because `WaitForCompletion` couples workflow duration to the dispatcher's connection -- fragile
  for long-running workflows, human-in-the-loop steps, or Elsa restarts.
* Bad, because `ReceiveMassTransitMessage` has known trigger reliability issues (Elsa GitHub #4968).
* Bad, because CorrelationId handling has gaps in concurrent dispatch chains (Elsa GitHub #6672).
* Bad, because SQL transport support in `Elsa.MassTransit` is not well-documented; the Elsa docs focus
  on RabbitMQ and Azure Service Bus.
* Bad, because outbox compatibility with SQL transport has known quirks (#6552, limited filter
  customization).

### Option C -- Hybrid: keep the Queue, replace the Processor with Elsa's dispatcher

The Queue API continues to own mTLS, audit, and outbox publishing. Instead of the Processor consuming
and dispatching over HTTP, Elsa's `IWorkflowDispatcher` (backed by MassTransit) consumes `ISubmitRequest`
directly.

* Good, because mTLS + audit + outbox remain broker-owned (the Queue is unchanged).
* Good, because one fewer service to operate (no Processor).
* Bad, because Elsa must consume `ISubmitRequest` -- coupling Elsa's consumer configuration to the
  broker's contract and transport.
* Bad, because the same `ReceiveMassTransitMessage` trigger reliability issues (#4968) and CorrelationId
  gaps (#6672) apply on the consumer side.
* Bad, because the boundary between "broker routing" and "Elsa execution" blurs -- debugging a failed
  request requires understanding both systems' MassTransit configurations.
* Bad, because EB-004 packaging becomes harder: the Processor package disappears, but Elsa becomes a
  hard dependency of any deployment.

## SWOT Analysis: Custom Topology vs Elsa's Built-In Dispatch

|                    | **Helpful**                                                                                                          | **Harmful**                                                                                                  |
|--------------------|----------------------------------------------------------------------------------------------------------------------|--------------------------------------------------------------------------------------------------------------|
| **Internal**       | **Strengths**                                                                                                        | **Weaknesses**                                                                                               |
| *Custom topology*  | Native mTLS ingress; first-class audit trail (`RequestRecord`); explicit transactional outbox; clear responsibility boundary (Queue / Processor / Elsa); transport-agnostic | Two extra services to operate; HTTP callback adds a network hop; custom dispatcher code to maintain           |
| *Elsa built-in*    | Less code (no `SubmitRequestConsumer` / handler layer); native workflow activities (`ReceiveMassTransitMessage`); Elsa manages discovery and dispatch; single deployment unit | No built-in audit trail; mTLS requires custom wrapper; `WaitForCompletion` fragility; SQL transport not well-documented; trigger reliability issues (#4968) |
| **External**       | **Opportunities**                                                                                                    | **Threats**                                                                                                  |
| *Custom topology*  | Structured logging (EB-002) integrates naturally with `RequestRecord`; clean package boundaries (EB-004); Blazor dashboard (EB-003) on `ILogShipper` | Operational complexity of multiple services at scale; SQL transport immaturity vs RabbitMQ / ASB              |
| *Elsa built-in*    | Fewer moving parts if Elsa's MassTransit maturity improves; native Elsa Studio tooling integration                   | Elsa MassTransit maturity concerns (trigger reliability, CorrelationId gaps); vendor coupling to Elsa's internal schema; SQL transport immaturity (shared) |

## Needs Mapping

| Need | Custom Topology (Option A) | Elsa Built-In (Option B) | Assessment |
|------|---------------------------|--------------------------|------------|
| **Dynamic request types** (drop a workflow, no redeploy) | `WorkflowFolderScanner` + `RequestTypeRegistry`; folder convention with `customProperties.requestType`, discovered at startup | `IWorkflowProvider` + folder discovery; automatic trigger registration from message types | **Tie** -- both folder-based with startup discovery |
| **Shared workflow config** (same folder feeds Elsa and broker) | `workflows/` scanned by Processor AND imported by Elsa server; shared `requestType` convention | Elsa reads `Workflows/` natively; no separate scanner needed | **Tie** -- same mechanism, slightly less code in Option B |
| **mTLS + audit + outbox independent of Elsa** | Queue handles all three; Elsa is pure execution with no awareness of the broker's security or audit model | Requires custom Kestrel middleware for mTLS; audit trail must be built as Elsa middleware or activity; outbox configuration must coexist with Elsa's own MassTransit bus | **Custom topology wins** -- separation is the design, not an afterthought |
| **Async callback (decoupled from WaitForCompletion)** | Explicit callback model; Elsa calls back independently; the broker and Elsa can fail/restart without coupling | `WaitForCompletion` couples workflow duration to dispatcher lifetime; alternative is polling, which duplicates existing status infrastructure | **Custom topology wins** -- resilience under independent failure |

## More Information

* [ADR-0001](0001-use-masstransit-sql-server-transport.md) -- MassTransit SQL transport choice
* [ADR-0002](0002-transactional-outbox-inbox.md) -- Transactional outbox/inbox for durable publishing
* [ADR-0006](0006-elsa-workflows-as-the-processing-model.md) -- Elsa 3 workflows as the processing model
* Elsa MassTransit activities: <https://docs.elsaworkflows.io/activities/masstransit>
* Elsa HTTP workflows tutorial: <https://docs.elsaworkflows.io/guides/http-workflows/tutorial>
* EB-001 gates EB-002 / EB-003 / EB-004 (see `docs/agile/backlog/README.md`)
