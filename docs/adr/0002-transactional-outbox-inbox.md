---
status: "accepted"
date: 2026-06-06
decision-makers: dotnet-architect, db-architect
consulted: security
informed: team
---

# Transactional outbox/inbox for durable publishing

## Context and Problem Statement

The Queue API must both record a request (write `RequestRecord`) and publish a message
(`ISubmitRequest`). If these are two independent operations, a crash between them leaves the audit trail
and the queue inconsistent — a request marked `Queued` that was never published, or vice versa. How do
we make the state change and the publish atomic?

## Decision Drivers

* Exactly the right messages get published — no lost or phantom messages.
* The audit trail and the queue can never disagree.
* Consumers can tolerate redelivery without double-processing.

## Considered Options

* **EF Core transactional outbox + inbox** (`AddEntityFrameworkOutbox` + `UseBusOutbox`)
* **Publish directly** inside the request handler, best-effort
* **Two-phase commit / distributed transaction** across DB and broker

## Decision Outcome

Chosen option: **EF Core transactional outbox + inbox**, because the publish is written to an outbox
table inside the *same* DB transaction as the business state change, then dispatched to the transport by
a background delivery service. The inbox deduplicates on the consumer side.

### Consequences

* Good, because state change and publish are atomic — the dual-write problem is eliminated.
* Good, because the inbox gives consumer-side dedup, supporting idempotent processing.
* Good, because it needs no distributed transaction coordinator.
* Bad, because there is a small delivery latency (the outbox is swept asynchronously).
* Bad, because the outbox/inbox tables must be included in migrations and maintained.

### Confirmation

The `InitialCreate` migration includes `InboxState`, `OutboxState`, and `OutboxMessage`. Consumer
idempotency under redelivery is covered by `Redelivery_of_same_correlation_id_stays_completed`.

## More Information

Side effects inside handlers must still be idempotent (keyed on `CorrelationId`); the outbox guarantees
*delivery* semantics, not handler-side effect idempotence.
