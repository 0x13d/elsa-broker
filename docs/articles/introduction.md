# Introduction

ElsaBroker is a **generic message-queuing layer**. It accepts typed requests from authenticated client
systems, queues them durably, processes them asynchronously, and lets clients poll for the outcome.

## The model

Every request, regardless of type, is the same envelope — [`ISubmitRequest`](xref:ElsaBroker.Contracts.ISubmitRequest):

| Field | Meaning |
|-------|---------|
| `CorrelationId` | unique id for the request; the key for status polling and idempotency |
| `ClientId` | **set from the client certificate**, never from the body |
| `RequestType` | which kind of work this is (e.g. `InvoiceProcess`) |
| `Keys` | the natural-key fields for this request type (validated against the registry) |
| `Payload` | optional extra data |
| `SubmittedAt` | submission timestamp |

A **request type** is defined by `(ClientId, RequestType)` and a set of required keys. Authorization
definitions come from `requestTypes.json` (shipped defaults) overlaid with database rows (per-client,
no redeploy) — see the [registry](xref:ElsaBroker.Data.Registry.RequestTypeRegistry). The **processing**
for each request type is an **Elsa 3 workflow** discovered from the shared `workflows/` folder — see
[Elsa integration](elsa-integration.md).

## The lifecycle

```text
client ──mTLS POST /requests──▶ Queue API
                                  │  validate against registry
                                  │  write RequestRecord (Queued)
                                  │  publish ISubmitRequest via EF outbox  ── same transaction
                                  ▼
                                SQL Server (transport)
                                  ▼
                                Processor consumes
                                  │  RequestRecord → Processing
                                  │  dispatch to the Elsa workflow for this
                                  │  request type (async — leaves Processing)
                                  ▼
                                Elsa runs the workflow
                                  │  callback → Queue (:5080, shared secret)
                                  │  RequestRecord → Completed | Faulted
                                  ▼
client ──mTLS GET /requests/{id}──▶ status + result
```

## What makes it trustworthy

- **Identity is cryptographic.** The Queue API requires a client certificate; the `ClientId` is taken
  from the validated certificate, so it cannot be spoofed in the request body.
- **The queue and the audit trail never disagree.** The status write and the message publish share one
  database transaction through the EF outbox.
- **No telemetry.** The only network dependencies are SQL Server and the bus.

Continue with [Getting started](getting-started.md), or read the [architecture](architecture.md).
