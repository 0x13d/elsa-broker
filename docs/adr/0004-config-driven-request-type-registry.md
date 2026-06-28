---
status: "accepted"
date: 2026-06-06
decision-makers: dotnet-architect, db-architect
consulted: security
informed: team
---

# Config + DB driven request-type registry

## Context and Problem Statement

The broker is generic: it must support many request types across many clients without a code change or
redeploy each time a new one is added. How are request types defined, validated, and authorized per
client?

## Decision Drivers

* Add a request type for a client without redeploying the services.
* Ship sensible defaults in source control.
* Validate required keys and authorize `(ClientId, RequestType)` consistently in both services.

## Considered Options

* **Two-tier registry**: `requestTypes.json` (shipped defaults) overlaid by database rows (per-client)
* **JSON only** — every change is a redeploy
* **Database only** — no in-repo defaults, harder to review/diff

## Decision Outcome

Chosen option: **two-tier registry**. `RequestTypeRegistry` loads the JSON defaults, then overlays
active database `RequestTypeDefinition` rows; **DB rows win on conflict**, so a client-specific override
needs no redeploy. Lookup and authorization are case-insensitive on `(ClientId, RequestType)`.

### Consequences

* Good, because defaults are reviewable in source, while per-client changes are data, not deploys.
* Good, because one registry serves validation in the Queue and dispatch context in the Processor.
* Bad, because there are two sources of truth to reason about; precedence (DB > JSON) must be clear.
* Neutral, because hot-reload from the DB is possible but must be invoked explicitly.

### Confirmation

`RequestTypeRegistryTests` covers JSON load, case-insensitive lookup, DB-overrides-JSON, and that
inactive DB rows are ignored.

## More Information

Implementation note: the registry's `Dictionary` is constructed with the default comparer, but keys are
lowercased on both insert and lookup, so matching is effectively case-insensitive. A follow-up could
simplify this by using a proper case-insensitive tuple comparer and dropping the manual lowercasing.
