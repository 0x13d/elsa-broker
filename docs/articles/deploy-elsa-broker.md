# Deploy ElsaBroker

This quickstart covers two paths to deploying ElsaBroker: consuming the NuGet packages in your own
code (custom shippers, message producers, service host wiring) and running the full stack via Docker
Compose.

## NuGet packages

ElsaBroker ships three packages (see [ADR-0008](../adr/0008-nuget-package-boundaries.md)). Choose the
package that matches your role:

| Package | Version | Who needs it |
|---------|---------|--------------|
| `ElsaBroker.Contracts` | 0.3.0 | Message producers and consumers — the `ISubmitRequest` envelope and `RequestStatus` constants. Zero NuGet dependencies. |
| `ElsaBroker.Abstractions` | 0.3.0 | Extensibility authors — `ILogShipper`, `BrokerLogEvent`, `IRequestHandler`, `RequestResult`. Depends only on `Contracts`. No EF Core, no MassTransit. |
| `ElsaBroker` | 0.3.0 | Service host authors — `BrokerDbContext`, migrations, registries, built-in log shippers. Pulls in EF Core 9 and MassTransit 8.3. |

Host applications (Queue, Processor, WorkflowServer) ship as Docker images, not NuGet packages.

### Installing packages

Message producer (needs the wire contract only):

```bash
dotnet add package ElsaBroker.Contracts --version 0.3.0
```

Custom log shipper or request handler author (extensibility, no infrastructure weight):

```bash
dotnet add package ElsaBroker.Abstractions --version 0.3.0
```

Service host (full infrastructure: DbContext, MassTransit, registries, built-in shippers):

```bash
dotnet add package ElsaBroker --version 0.3.0
```

### Example: implementing a custom ILogShipper

Reference only `ElsaBroker.Abstractions` — no EF Core or MassTransit on the dependency graph:

```bash
dotnet add package ElsaBroker.Abstractions --version 0.3.0
```

```csharp
using ElsaBroker.Abstractions;

public class DatadogLogShipper : ILogShipper
{
    private readonly IHttpClientFactory _http;

    public DatadogLogShipper(IHttpClientFactory http) => _http = http;

    public async Task ShipAsync(BrokerLogEvent logEvent, CancellationToken ct = default)
    {
        // Forward to your observability platform.
        // BrokerLogEvent carries EventType, CorrelationId, ClientId, RequestType, and Timestamp.
        var client = _http.CreateClient("datadog");
        await client.PostAsJsonAsync("/api/v2/logs", new
        {
            ddsource  = "elsa-broker",
            ddtags    = $"request_type:{logEvent.RequestType},client:{logEvent.ClientId}",
            message   = logEvent.EventType,
            correlationId = logEvent.CorrelationId,
        }, ct);
    }
}
```

Register the shipper in the host's `Program.cs` (which references `ElsaBroker`):

```csharp
builder.Services.AddLogShipper<DatadogLogShipper>();
```

See [Structured logging](structured-logging.md) for the full shipper API and built-in options.

### Example: wiring up a service host

A service host that references the `ElsaBroker` meta-package gets `BrokerDbContext`, MassTransit SQL
transport, and the registry in one `dotnet add package` call:

```bash
dotnet add package ElsaBroker --version 0.3.0
```

Minimal `Program.cs` fragment for a worker host:

```csharp
using ElsaBroker.Data;
using MassTransit;

var builder = Host.CreateApplicationBuilder(args);

// EF Core + broker schema
builder.Services.AddDbContext<BrokerDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("Broker")));

// MassTransit SQL transport + EF outbox
builder.Services.AddMassTransit(mt =>
{
    mt.AddEntityFrameworkOutbox<BrokerDbContext>(o => o.UseSqlServer().UseBusOutbox());
    mt.UsingMassTransitSqlServer((ctx, cfg) =>
    {
        cfg.Host(builder.Configuration.GetConnectionString("Broker"));
        cfg.ConfigureEndpoints(ctx);
    });
});

builder.Build().Run();
```

---

## Docker deployment

The full stack runs from a single Compose file. The host services (Queue, Processor, WorkflowServer)
ship as Docker images built from the project Dockerfiles; SQL Server and Elsa Studio are off-the-shelf
images.

### Prerequisites

- **Docker Desktop** (4.x or later). On Apple Silicon, enable
  **Settings ▸ General ▸ "Use Rosetta for x86_64/amd64 emulation"** — SQL Server requires it
  ([ADR-0005](../adr/0005-sql-server-on-apple-silicon-via-rosetta.md)).
- **.NET 9 SDK** — required only to build the broker images locally.
  On this machine: `export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"`.

### Bring up the reference stack

All commands below run from `src/services/ElsaBroker/`.

```bash
docker compose -f docker-compose.reference.yml up -d
docker compose -f docker-compose.reference.yml ps   # wait until all services are healthy
```

### Services

| Service | Port | Role |
|---------|------|------|
| `sqlserver` | `localhost:1433` | SQL Server 2022 — broker database, MassTransit SQL transport, EF outbox/inbox |
| `elsa-broker-queue` | `localhost:5001` (mTLS), `localhost:5080` (callback) | Queue API — mTLS ingress, validation, enqueue, callback finalization |
| `elsa-broker-processor` | _(internal)_ | Processor — consumes from SQL transport, dispatches to Elsa |
| `elsa-server` | `localhost:13002` | Custom ElsaBroker.WorkflowServer — auto-imports `workflows/` on startup |
| `elsa-studio` | `http://localhost:13000` | Elsa Studio UI — pointed at the custom server |

> The Queue and Processor containers share an internal Docker network. The Queue's callback listener
> (`:5080`) is exposed on the host so Elsa's `SendHttpRequest` activity can reach it via
> `host.docker.internal:5080`.

### Configuration via environment variables

All `appsettings.json` values are overridable through environment variables using the standard
ASP.NET Core double-underscore (`__`) separator.

**Queue service:**

| Variable | Default | Description |
|----------|---------|-------------|
| `ConnectionStrings__Broker` | _(required)_ | SQL Server connection string |
| `Mtls__CaThumbprint` | _(required)_ | SHA-1 thumbprint of the internal CA certificate |
| `Elsa__CallbackSecret` | `dev-callback-secret-change-me` | Shared secret validated on the callback endpoint |
| `LogShipper__Sink` | _(empty — null shipper)_ | `Console` or `Http` |
| `LogShipper__SinkUrl` | _(required if Sink=Http)_ | Target URL (Seq, Logstash, OpenSearch, etc.) |

**Processor service:**

| Variable | Default | Description |
|----------|---------|-------------|
| `ConnectionStrings__Broker` | _(required)_ | SQL Server connection string |
| `Elsa__ServerUrl` | `http://elsa-server:8080` | Base URL of the ElsaBroker.WorkflowServer |
| `Elsa__CallbackSecret` | `dev-callback-secret-change-me` | Must match the Queue's value and the workflow header |
| `Elsa__WorkflowsPath` | `workflows/` | Path to the folder the scanner reads for `requestType` mappings |
| `LogShipper__Sink` | _(empty — null shipper)_ | `Console` or `Http` |

> The callback secret (`Elsa:CallbackSecret`) must be identical in the Queue, the Processor, and the
> `X-Callback-Secret` header set by the `broker-dispatch` workflow. The workflow is the source of the
> callback call; all three must agree or the Queue will reject the finalization.

### Certificates

The Queue requires mTLS. Generate the CA, server cert, and at least one client cert before starting
the stack. From `src/services/ElsaBroker/`:

```bash
cd ElsaBroker.CertTools
dotnet run -- ca
dotnet run -- server localhost
dotnet run -- client ClientA
```

Mount the `certs/` directory into the Queue container and set:

```yaml
environment:
  Mtls__CaThumbprint: "<ca-thumbprint>"
volumes:
  - ./ElsaBroker.Queue/certs:/app/certs:ro
```

Add the client thumbprint to `ElsaBroker.Queue/ClientAllowlist.json` and mount that file into the
container. See [Security model](security-model.md) for the full allowlist format.

### Submit a first request

With the stack running and certificates in place, submit an `InvoiceProcess` request using the
bundled smoke-client (macOS `curl` cannot present a PEM client cert in the handshake):

```bash
python3 docs/scripts/smoke-client.py
```

The script submits over mutual TLS, then polls `GET /requests/{correlationId}` every second until the
status reaches `Completed` or `Faulted`. Expected output:

```text
POST /requests -> 202 {'correlationId': 'xxxxxxxx-...', 'status': 'Queued'}
GET /requests/xxxxxxxx-... -> Processing
GET /requests/xxxxxxxx-... -> Completed
{
  "correlationId": "xxxxxxxx-...",
  "status": "Completed",
  ...
}
```

The lifecycle: `Queued → Processing → (Elsa runs the workflow → callback) → Completed | Faulted`.

---

## Configuration reference

### `appsettings.json` sections

**`ConnectionStrings`**

```json
{
  "ConnectionStrings": {
    "Broker": "Server=localhost,1433;Database=ElsaBroker;User Id=sa;Password=...;TrustServerCertificate=True"
  }
}
```

**`Mtls`**

```json
{
  "Mtls": {
    "CaThumbprint": "<hex-sha1-thumbprint-of-internal-ca>"
  }
}
```

**`Elsa`**

```json
{
  "Elsa": {
    "ServerUrl": "http://localhost:13002",
    "CallbackSecret": "change-me-in-production",
    "WorkflowsPath": "workflows/"
  }
}
```

**`LogShipper`**

```json
{
  "LogShipper": {
    "Sink": "Http",
    "SinkUrl": "http://localhost:5341/api/events/raw",
    "ApiKey": ""
  }
}
```

See [Structured logging](structured-logging.md) for sink options and custom shipper registration.

### `requestTypes.json` format

Defines which `(ClientId, RequestType)` pairs are authorized and what keys each request must carry.
Loaded by `RegistryLoader` at startup and merged with any database rows:

```json
[
  {
    "clientId": "ClientA",
    "requestType": "InvoiceProcess",
    "requiredKeys": ["InvoiceNumber", "VendorCode"]
  },
  {
    "clientId": "ClientA",
    "requestType": "PaymentApproval",
    "requiredKeys": ["PaymentId"]
  }
]
```

The file is typically placed at the application root and its path is configured via
`Registry:RequestTypesFile` (defaults to `requestTypes.json`). New entries take effect on restart; no
migration is required for file-only entries.

### `ClientAllowlist.json` format

Maps client certificate thumbprints to logical `ClientId` values. Only certificates whose thumbprint
appears in this file pass the mTLS authorization check:

```json
[
  {
    "thumbprint": "<hex-sha1-thumbprint>",
    "clientId": "ClientA"
  }
]
```

Rotation: issue a new client cert with `ElsaBroker.CertTools`, add its thumbprint here, and remove
the old entry once the client has rotated. Revocation is instant — removing an entry from this file
and restarting the Queue denies that certificate immediately.

See [Security model](security-model.md) for the full mTLS authorization flow and CA chain validation.
