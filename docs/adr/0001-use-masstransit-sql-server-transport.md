---
status: "accepted"
date: 2026-06-06
decision-makers: dotnet-architect, db-architect
consulted: security
informed: team
---

# Use MassTransit on the SQL Server transport

## Context and Problem Statement

ElsaBroker needs durable, asynchronous request delivery between the Queue API and the Processor. How
should messages be transported, and on what infrastructure, given the system already requires a SQL
Server database for its audit trail?

## Decision Drivers

* Durability — a request accepted by the API must not be lost.
* Operational simplicity — minimize the number of stateful systems to run and secure.
* Transactional consistency with the audit store (see [ADR-0002](0002-transactional-outbox-inbox.md)).
* No telemetry / self-contained footprint.

## Considered Options

* MassTransit on the **SQL Server transport**
* MassTransit on **RabbitMQ**
* MassTransit on **Azure Service Bus**

## Decision Outcome

Chosen option: **MassTransit on the SQL Server transport**, because it lets the queue, the outbox/inbox,
and the audit trail share a single database — one transaction boundary, one backup, one operational
surface — without standing up a separate broker.

### Consequences

* Good, because enqueue + audit write commit in one transaction (no dual-write).
* Good, because there is no extra broker to deploy, secure, patch, or monitor.
* Good, because MassTransit keeps the option open to switch transports later behind the same abstractions.
* Bad, because raw throughput is lower than a dedicated broker; SQL Server becomes the scaling pivot.
* Bad, because operators must understand the transport tables alongside the domain tables.

### Confirmation

`ElsaBroker.Tests` exercises the consumer via the MassTransit in-memory harness. The live SQL-Server
transport path is confirmed by running both services against the Docker SQL Server and observing a
request flow end-to-end (`Queued → Completed`).

## More Information

The original scaffold called `mt.UsingMsSql(...)` and `sql.Host(connectionString)` — **neither exists**
in MassTransit 8.3. The correct API is `mt.UsingSqlServer(...)` with the connection supplied via
`SqlTransportOptions.ConnectionString`, and `AddSqlServerMigrationHostedService()` to provision the
transport schema. This was found and fixed while making the solution build.
