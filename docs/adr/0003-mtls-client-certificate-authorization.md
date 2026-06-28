---
status: "accepted"
date: 2026-06-06
decision-makers: security, dotnet-architect
consulted: db-architect
informed: team
---

# mTLS client-certificate authorization

## Context and Problem Statement

ElsaBroker is called by other server systems, not interactive users. Each request must be attributed to a
specific client, and a client must not be able to impersonate another or submit request types it isn't
authorized for. How should callers authenticate and how should their identity be established?

## Decision Drivers

* Strong, non-spoofable identity for server-to-server calls.
* No shared secrets to distribute and rotate.
* Identity must be impossible to forge in the request body.
* Fits a no-telemetry, self-contained deployment.

## Considered Options

* **Mutual TLS** with an internal CA and a thumbprint allowlist
* **API keys / bearer tokens** per client
* **OAuth2 client credentials** via an external identity provider

## Decision Outcome

Chosen option: **mutual TLS**, because the client's identity is the certificate. Kestrel requires a
client cert; `MtlsAuthHandler` validates the CA chain, validity window, and allowlist thumbprint, then
stamps `ClientId` from the certificate. `RequestEndpoints` uses that claim for authorization and record
ownership — `ClientId` is never read from the body.

### Consequences

* Good, because identity cannot be spoofed in the request payload.
* Good, because rotation is "issue cert + update allowlist", and revocation is "remove from allowlist" —
  no shared-secret coordination.
* Good, because no external identity provider is required (self-contained, no telemetry).
* Bad, because certificate distribution and lifecycle is an operational burden (mitigated by
  `ElsaBroker.CertTools`).
* Bad, because misconfigured CA/allowlist fails closed — clients are rejected until configured.

### Confirmation

Review that no code path reads `ClientId` from the request body; verify `RequireCertificate` is set and
that chain + validity + allowlist checks all gate the request. Polling is scoped to the caller's own
records.

## More Information

`certs/`, `*.pfx`, and key material are git-ignored. See the [security model](../articles/security-model.md).
