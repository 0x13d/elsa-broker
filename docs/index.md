---
_layout: landing
---

# ElsaBroker

**A durable, mTLS-secured queueing front end for Elsa 3 workflows.**

Clients submit typed requests over an HTTP API authenticated by client certificates. The broker owns
ingress, authorization, durable delivery (MassTransit over the SQL Server transport), and a complete
audit trail — and **dispatches each request type to an [Elsa 3](https://elsaworkflows.io) workflow** for
the actual work. Workflows are versioned in git, authored visually in Elsa Studio, and added by dropping
a JSON file in a shared folder — no handler code to write. Built for **resiliency** (transactional
outbox/inbox, async callbacks) and **trust** (authorization asserted by the certificate, never the
caller).

<div class="hero-buttons">

[Get started](articles/getting-started.md) ·
[Elsa integration](articles/elsa-integration.md) ·
[Deployment](articles/elsa-deployment.md) ·
[Security model](articles/security-model.md) ·
[Decisions](adr/index.md) ·
[API reference](xref:ElsaBroker.Contracts)

</div>

## At a glance

| | |
|---|---|
| **Runtime** | .NET 9 |
| **Processing** | Elsa 3 workflows (remote server + Studio); folder-loaded, request-type convention |
| **Messaging** | MassTransit 8.3 · SQL Server transport · EF outbox/inbox |
| **Security** | mTLS — Kestrel `RequireCertificate`, internal CA, client allowlist; shared-secret callback |
| **Audit** | every request recorded `Queued → Processing → Completed / Faulted` |

## Why it exists

Teams that need "send a job to a queue, let a workflow handle it, and check on it later" end up
rebuilding the same plumbing: authentication, a request envelope, a type registry, durable delivery, and
an audit trail — then bolting on a workflow engine. ElsaBroker packages the plumbing once and makes
**Elsa the unit of work**:

- **One envelope** (`ISubmitRequest`) carries every request type. A new request type is just a workflow
  JSON in the shared folder declaring `customProperties.requestType` — the broker auto-registers it.
- **The certificate is the identity.** A client can only submit the request types it is registered for,
  and can only read its own requests. `ClientId` is never taken from the request body.
- **Durable by construction.** Status changes and publishes commit in one database transaction (EF
  outbox); workflows run asynchronously and finalize the request via a callback.

## How the pieces fit

See the [deployment diagram](articles/elsa-deployment.md) for the broker ⇄ Elsa ⇄ SQL runtime topology,
and [Elsa integration](articles/elsa-integration.md) for the folder convention and async-callback model.
Diagrams are generated offline by the sibling tools [`netjson-diagrams`](https://ariugwu.com/netjson)
and [`elsa-to-mermaid`](https://ariugwu.com/elsa).
