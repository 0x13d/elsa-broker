# Architectural Decision Records

This log captures the significant decisions behind ElsaBroker, using the
[MADR](https://adr.github.io/madr/) format. Each record is immutable once accepted; to change a
decision, add a new ADR that supersedes the old one.

Start a new record by copying [`adr-template.md`](adr-template.md) to the next number.

| # | Decision | Status |
|---|----------|--------|
| [0001](0001-use-masstransit-sql-server-transport.md) | Use MassTransit on the SQL Server transport | Accepted |
| [0002](0002-transactional-outbox-inbox.md) | Transactional outbox/inbox for durable publishing | Accepted |
| [0003](0003-mtls-client-certificate-authorization.md) | mTLS client-certificate authorization | Accepted |
| [0004](0004-config-driven-request-type-registry.md) | Config + DB driven request-type registry | Accepted |
| [0005](0005-sql-server-on-apple-silicon-via-rosetta.md) | Local SQL Server on Apple Silicon via Rosetta | Accepted |
| [0006](0006-elsa-workflows-as-the-processing-model.md) | Elsa 3 workflows as the processing model (remote dispatch + async callback) | Accepted |
| [0007](0007-custom-masstransit-topology-vs-elsa-built-in-dispatch.md) | Custom MassTransit topology vs Elsa's built-in dispatch (EB-001) | Accepted |
| [0008](0008-nuget-package-boundaries.md) | NuGet package boundaries (three packages: Contracts, Abstractions, ElsaBroker) (EB-004) | Accepted |
| [0009](0009-plugin-system.md) | Plugin system for external workflow provisioning (EB-007a) | Accepted |

> Candidate ADRs not yet written: retry/redelivery & circuit-breaker policy; ordering/partitioning
> guarantee; at-rest considerations for the audit store.
