---
status: "accepted"
date: 2026-06-06
decision-makers: dotnet-architect, db-architect
consulted: security
informed: team
---

# Local SQL Server on Apple Silicon via Rosetta (not SQL Edge)

## Context and Problem Statement

The broker targets real SQL Server (MassTransit SQL transport + EF Core). On the Apple-Silicon (arm64)
development machine, the official `mcr.microsoft.com/mssql/server` image is amd64-only and, under Docker's
default QEMU emulation, `sqlservr` crashes on startup:

```
/opt/mssql/bin/sqlservr: Invalid mapping of address ... in reserved address space below 0x400000000000
```

How do we get a local database to develop and validate against that is faithful to the production engine?

## Decision Drivers

* Faithfulness to the production target (real SQL Server engine + T-SQL surface).
* MassTransit's SQL transport must work (it provisions schema with engine-specific T-SQL).
* Must actually run on this arm64 machine.
* Low ongoing maintenance / not a dead end.

## Considered Options

* **Real SQL Server 2022 image run via Docker Desktop's Rosetta** x86-64 emulation
* **Azure SQL Edge** (arm64-native)
* **QEMU emulation** of the SQL Server image (Docker default)
* A remote/managed SQL Server instance

## Decision Outcome

Chosen option: **real SQL Server 2022 via Rosetta**. Enable Docker Desktop ▸ Settings ▸ General ▸
"Use Rosetta for x86_64/amd64 emulation", pin the service to `platform: linux/amd64`, and the official
image runs correctly. This keeps us on the exact production engine with the full T-SQL surface.

Azure SQL Edge was **rejected**: it is retired/deprecated, is not the production engine, and may not
fully support the T-SQL the MassTransit SQL transport emits — validating against it would prove little.
Plain QEMU is a non-starter (the crash above).

### Consequences

* Good, because development and CI validate against the real SQL Server engine.
* Good, because MassTransit SQL transport + EF migrations behave exactly as in production.
* Bad, because it requires the Rosetta toggle (a one-time Docker Desktop setting) and emulation has some
  CPU overhead.
* Neutral, because `docker-compose.yml` pins `platform: linux/amd64`; on amd64 hosts this is a no-op.

### Confirmation

Validated end-to-end on 2026-06-06: SQL Server 2022 reached `healthy` under Rosetta in ~18s; the Queue
applied the EF `InitialCreate` migration, the MassTransit SQL transport provisioned its schema and
reported `Bus started: db://localhost:1433/`; a request submitted over mTLS as ClientA flowed
`Queued → Completed` with the Processor's handler result, and the `RequestRecords` audit row confirmed
`ClientA / InvoiceProcess / Completed`.

## More Information

Bringing the mTLS path up live surfaced four more bugs in the original scaffold (all fixed; see the
CHANGELOG): the cert tool used `EphemeralKeySet` (unsupported on macOS) and omitted the Authority Key
Identifier extension (rejected by OpenSSL 3); Kestrel required a client certificate but supplied no
`ClientCertificateValidation` callback, so internal-CA certs were rejected at the TLS layer before the
app's handler ran; and `MtlsAuthHandler` opened the `LocalMachine\CA` store, which macOS does not
support, instead of falling back to the on-disk CA.
