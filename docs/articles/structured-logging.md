# Structured logging

ElsaBroker ships structured broker-domain events — request lifecycle transitions like `RequestSubmitted`,
`RequestProcessing`, `RequestDispatched`, `RequestCompleted`, and `RequestFaulted` — to pluggable sinks
via the `ILogShipper` abstraction. This supplements (does not replace) the standard
`Microsoft.Extensions.Logging` output.

## How it works

Each service (Queue, Processor) registers an `ILogShipper` at startup. At key pipeline points, a
`BrokerLogEvent` is created with structured fields (CorrelationId, ClientId, RequestType, etc.) and
shipped to the configured sink. The shipper never throws — failures are logged as warnings but do not
interrupt the request pipeline.

## Configuration

Set `LogShipper:Sink` in `appsettings.json` (or environment variables) to choose a shipper:

| Value | Shipper | Description |
|-------|---------|-------------|
| _(empty)_ | `NullLogShipper` | Default — events are discarded |
| `Console` | `ConsoleLogShipper` | Structured JSON to stdout (good for dev, container log scrapers) |
| `Http` | `HttpJsonLogShipper` | POST JSON to `LogShipper:SinkUrl` (Seq, Logstash, OpenSearch, etc.) |

Example for Seq:

```json
{
  "LogShipper": {
    "Sink": "Http",
    "SinkUrl": "http://localhost:5341/api/events/raw",
    "ApiKey": ""
  }
}
```

## Running the Seq sink (optional compose profile)

The Docker lab includes an opt-in [Seq](https://datalust.co/seq) service behind a compose profile:

```bash
docker compose --profile logging up -d
```

This starts Seq alongside the existing services:

| Service | Where | What |
|---------|-------|------|
| `seq` | http://localhost:8081 | Seq UI — search, filter, dashboard structured events |
| _(ingestion)_ | `localhost:5341` | Seq ingestion API (the `SinkUrl` target) |

To enable shipping from the broker services, set `LogShipper:Sink=Http` in both Queue and Processor
`appsettings.json` (or via environment variables: `LogShipper__Sink=Http`).

## Event types

The following event types are shipped from the request pipeline:

| EventType | Source | When |
|-----------|--------|------|
| `RequestSubmitted` | Queue | Client submits a request over mTLS |
| `CallbackReceived` | Queue | Elsa workflow calls back with the result |
| `RequestProcessing` | Processor | Consumer picks up the message, sets status to Processing |
| `RequestDispatched` | Processor | Handler dispatches to Elsa, returns Deferred |
| `RequestCompleted` | Processor | Handler returns success (synchronous handlers only) |
| `RequestFaulted` | Processor | Handler fails, throws, or no handler found |

Each event carries `CorrelationId`, `ClientId`, and `RequestType` for filtering and correlation.

## Writing a custom shipper

Implement `ILogShipper` and register it at startup:

```csharp
public class MyCustomShipper : ILogShipper
{
    public async Task ShipAsync(BrokerLogEvent logEvent, CancellationToken ct = default)
    {
        // Ship the event to your sink (Splunk, Datadog, a database, etc.)
    }
}

// In Program.cs:
builder.Services.AddLogShipper<MyCustomShipper>();
```

Or use the `HttpJsonLogShipper` with a custom URL:

```csharp
builder.Services.AddHttpJsonLogShipper(o =>
{
    o.SinkUrl = "https://my-logstash:9200/_doc";
});
```

See the [architecture](architecture.md) for where logging fits in the request path, and the
[messaging design](messaging-design.md) for how CorrelationId flows end-to-end.
