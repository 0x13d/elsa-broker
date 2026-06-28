# Messaging design

This is the heart of ElsaBroker: how messages are delivered durably and processed safely. The decisions
below are owned by the **.NET architect** and recorded as ADRs.

## Bus configuration

Both services configure MassTransit over the SQL Server transport:

```csharp
builder.Services.AddOptions<SqlTransportOptions>().Configure(o =>
{
    o.ConnectionString = cfg.GetConnectionString("BrokerDb");
});
builder.Services.AddSqlServerMigrationHostedService();   // provisions transport schema

builder.Services.AddMassTransit(mt =>
{
    mt.AddEntityFrameworkOutbox<BrokerDbContext>(o =>
    {
        o.UseSqlServer();
        o.UseBusOutbox();
    });
    mt.UsingSqlServer((ctx, sql) => sql.ConfigureEndpoints(ctx));  // Queue
    // Processor additionally: mt.AddConsumer<SubmitRequestConsumer>() and a receive endpoint
});
```

> The original scaffold used `mt.UsingMsSql(...)` and `sql.Host(connectionString)` — neither exists in
> MassTransit 8.3. The correct surface is `UsingSqlServer` plus `SqlTransportOptions`. See
> [ADR-0001](../adr/0001-use-masstransit-sql-server-transport.md).

## Transactional outbox / inbox

`AddEntityFrameworkOutbox` + `UseBusOutbox()` mean a publish is written to the **outbox table inside the
same DB transaction** as the business state change, then dispatched to the transport by a background
delivery service. This removes the classic dual-write race: either both the audit row and the queued
message are committed, or neither is. The inbox provides consumer-side dedup. Rationale in
[ADR-0002](../adr/0002-transactional-outbox-inbox.md).

## Dispatch & deferral (Elsa)

`SubmitRequestConsumer` no longer finalizes the record itself — it **dispatches to Elsa** and waits for a
callback (see [Elsa integration](elsa-integration.md)):

1. Set `RequestRecord` → `Processing`.
2. Resolve the `ElsaDispatchHandler` for the `RequestType` (auto-registered from the `workflows/` folder).
   If none is registered, the record is `Faulted` ("no handler").
3. The handler POSTs to the Elsa **broker-dispatch** workflow and returns `RequestResult(Deferred: true)`.
   The consumer **returns without finalizing** — the record stays `Processing`.
4. The Elsa workflow finalizes the record later via the callback endpoint.

## Idempotency & redelivery

Consumers must assume **at-least-once** delivery; everything keys on `CorrelationId`:

- If no `RequestRecord` exists for the id, the consumer logs and returns (nothing to do).
- For built-in (non-deferred) handlers, redelivery re-runs the handler and the record converges on the
  same terminal state — verified by `Redelivery_of_same_correlation_id_stays_completed`.
- For Elsa dispatch, redelivery re-POSTs to the dispatcher. The broker passes `CorrelationId` as the
  workflow correlation id so Elsa can resolve the **same workflow instance** rather than starting a
  duplicate; the audit record is keyed on it either way. Making dispatch strictly idempotent under
  redelivery (dedup at the dispatcher) is tracked hardening.

## Failure handling

| Failure | Current behavior | Hardening (tracked) |
|---------|------------------|---------------------|
| No workflow registered for type | record → `Faulted` ("no handler") | reject earlier at the registry/ingress |
| Dispatch POST fails | consumer throws → message retried/redelivered | classify transient vs poison; `UseMessageRetry` + `UseDelayedRedelivery` |
| Workflow faults | callback sets record → `Faulted` with the workflow error | surface workflow incident detail |
| Callback never arrives | record stuck in `Processing` | a reaper/timeout that faults stale `Processing` records |
| Overload | (none yet) | bounded concurrency + 429 backpressure at ingress |

The retry/redelivery/circuit-breaker policies are the next architecture work — they belong here and in a
future ADR, configured with MassTransit's `UseMessageRetry`, `UseDelayedRedelivery`, and a dead-letter
path.

## Scaling

- **Concurrency:** tune `PrefetchCount` / `ConcurrentMessageLimit` on the receive endpoint.
- **Partitioning:** when ordering matters within a key (e.g. per `ClientId`) but not across keys, use a
  partitioner so independent keys process in parallel while a single key stays ordered.
- **Document the guarantee.** State the actual ordering and delivery guarantee you provide; don't imply
  exactly-once where it's at-least-once + idempotent.
