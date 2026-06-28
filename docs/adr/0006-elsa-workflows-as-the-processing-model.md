---
status: "accepted"
date: 2026-06-07
decision-makers: dotnet-architect, docs-web
consulted: security, db-architect
informed: team
---

# Elsa 3 workflows as the processing model (remote dispatch + async callback)

## Context and Problem Statement

The broker is generic: every request type needs *some* processing logic. Originally this was a
hand-written C# `IRequestHandler` per type. We want request processing to be **versioned in git,
authored visually, and diagrammable**, and we want to add a request type without writing or redeploying
handler code. How should request types be implemented?

## Decision Drivers

* Author/version/operate processing as workflows, not bolted-on code.
* Add a request type without a code change to the broker.
* Support long-running work (timers, human approval) without blocking a consumer.
* Keep the broker's mTLS ingress + audit guarantees intact.
* A tight authoring feedback loop (edit → run → refine).

## Considered Options

* **Hand-written C# `IRequestHandler` per type** (the original model)
* **Embedded (in-process) Elsa** — the Processor runs workflows in-process
* **Remote Elsa server + shared workflow folder + async callback** (Elsa 3 only)

## Decision Outcome

Chosen: **remote Elsa 3 server + shared workflow folder + async callback.**

- A shared `workflows/` folder holds Elsa 3 JSON definitions. Each request-handling workflow declares
  `customProperties.requestType`. The Elsa server mounts the folder (`/app/Workflows`); the broker scans
  the same folder and auto-registers `requestType → definitionId` (`WorkflowFolderScanner`).
- The Processor dispatches to a single Elsa **broker-dispatch** workflow, which runs the target workflow
  by id and **calls back** the broker to finalize. The dispatch handler returns *deferred*; the consumer
  leaves the record `Processing` until the callback arrives.
- The callback hits a dedicated internal listener (`:5080`) authenticated by a **shared secret**, not
  mTLS — the Elsa container can't readily present a client certificate, and the callback is an internal
  server-to-server hop. The public mTLS ingress (`:5001`) is unchanged.
- **Elsa 3 only.** Elsa 2 is not targeted (its mTLS/security story is undocumented; v3 is the supported
  engine — see [ADR-0003](0003-mtls-client-certificate-authorization.md) for the broker's own mTLS).

### Consequences

* Good, because adding a request type is "drop a workflow JSON in the folder" — no handler code, no redeploy.
* Good, because workflows are versioned, visually authored (Elsa Studio), and diagrammable (elsa-to-mermaid).
* Good, because async callback supports long-running/human-in-the-loop workflows without holding a consumer.
* Bad, because there is now a second runtime (the Elsa server) and two extra network hops to operate.
* Bad, because the callback's shared secret is weaker than mTLS; it must stay on the internal network.
* Neutral, because the broker-dispatch workflow is hand-authored once, then refined in Studio.

### Confirmation

`WorkflowFolderScannerTests` cover the folder convention. End-to-end: with the Docker stack up
(SQL + Elsa Server/Studio) and the broker running, a submitted request flows
`Queued → Processing → (Elsa) → callback → Completed`, confirmed via `docs/scripts/smoke-client.py`.

## More Information

The Elsa server+studio image is multi-arch; run the **native arm64** image on Apple Silicon (the amd64
image crashes under Rosetta's JIT). SQL Server still needs Rosetta — see
[ADR-0005](0005-sql-server-on-apple-silicon-via-rosetta.md). Next: a `pivlib`-backed activity so
workflows can interrogate Base64 PKI/PIV payloads in messages.
