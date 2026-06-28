# Getting started

This walks the full local lab: bring up SQL Server + Elsa (custom server + Studio), author workflows in
Studio, run the broker, and push a request through Elsa end-to-end.

## Prerequisites

- **.NET 9 SDK** — on this machine it lives under `~/.dotnet` (the system SDK is 8.0 and can't target
  `net9.0`). Put it first on `PATH`, and set `DOTNET_ROOT` so the global tools resolve the 9.0 runtime:
  ```bash
  export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"
  export DOTNET_ROOT="$HOME/.dotnet"
  ```
- **Docker Desktop.** On Apple Silicon, enable **Settings ▸ General ▸ "Use Rosetta for x86_64/amd64
  emulation"** — SQL Server is amd64-only and needs it ([ADR-0005](../adr/0005-sql-server-on-apple-silicon-via-rosetta.md)).
  The Elsa images are multi-arch and run native arm64.

All commands below run from `src/services/ElsaBroker/`.

## 1. Bring up the lab (SQL + Elsa)

```bash
docker compose up -d
docker compose ps          # wait for elsa-broker-sql = healthy
```

This starts three services:

| Service | Where | What |
|---------|-------|------|
| `elsa-broker-sql` | `localhost:1433` | SQL Server — broker DB + MassTransit SQL transport |
| `elsa-server` | `localhost:13002` | Custom **ElsaBroker.WorkflowServer** — imports `workflows/` on startup |
| `elsa-studio` | http://localhost:13000 | Elsa **Studio** UI — pointed at the custom server |

## 2. Author workflows in Elsa Studio

1. Open **http://localhost:13000** and sign in with **`admin` / `password`** (default dev credentials —
   change for anything non-local).
2. **Build or refine the workflows in Studio.** The custom `ElsaBroker.WorkflowServer` auto-loads the
   `workflows/` folder on startup via `WorkflowFolderImporter`, so any workflow JSON already in the
   folder is available immediately. Build **`broker-dispatch`** to the contract in
   `workflows/README.md` (ack 202 → `DispatchWorkflow` the target with `WaitForCompletion` →
   `SendHttpRequest` the result to `callbackUrl` with header `X-Callback-Secret`), and one workflow per
   request type (e.g. `invoice-process` with `definitionId = invoice-process`).
3. **Export** each workflow's JSON into `workflows/` (the broker scans that folder to register
   `requestType → definitionId`; declare `customProperties.requestType` on each request-type workflow).
   Restart the Elsa server container to pick up newly exported files; the broker re-registers on next
   start.

> Each request-handling workflow must declare `customProperties.requestType`; see
> [Elsa integration](elsa-integration.md) for the convention.

## 3. Certificates (one-time)

The Queue API requires client certificates signed by an internal CA:

```bash
cd ElsaBroker.CertTools
dotnet run -- ca                # internal CA (run once) — note the thumbprint
dotnet run -- server localhost  # server cert for the Queue
dotnet run -- client ClientA    # one client cert per authorized caller — note the thumbprint
cd ..
```

Then:

- Put the **CA thumbprint** in `ElsaBroker.Queue/appsettings.json` → `Mtls:CaThumbprint`.
- Add the **client thumbprint** to `ElsaBroker.Queue/ClientAllowlist.json`.
- Copy `ca.crt` next to the Queue binary so chain validation finds it:
  ```bash
  mkdir -p ElsaBroker.Queue/bin/Debug/net9.0/certs
  cp ElsaBroker.Queue/certs/ca.crt ElsaBroker.Queue/bin/Debug/net9.0/certs/
  ```

Details: [security model](security-model.md).

## 4. Build + run the broker

The Elsa server is now a container started by `docker compose up -d` (step 1 above) and runs on port
13002. You only need to run the two .NET broker services locally:

```bash
dotnet build                 # all projects (0 warnings)
dotnet test                  # xUnit + coverage

# terminal 1 — Queue API: mTLS ingress :5001, internal callback listener :5080
cd ElsaBroker.Queue && dotnet run

# terminal 2 — Processor: consumes, dispatches to Elsa, awaits callback
cd ElsaBroker.Processor && dotnet run
```

On startup the Processor logs how many request types it registered from `workflows/`. EF migrations and
the SQL transport schema are applied automatically.

> The shared **callback secret** must match in both `ElsaBroker.Queue/appsettings.json` and
> `ElsaBroker.Processor/appsettings.json` (`Elsa:CallbackSecret`) and in the `X-Callback-Secret` header
> the `broker-dispatch` workflow sends. The default dev value is `dev-callback-secret-change-me`.

## 5. Submit a request (end-to-end through Elsa)

macOS `curl` (SecureTransport) can't present a PEM client cert, so use the bundled Python client:

```bash
python3 docs/scripts/smoke-client.py
```

It submits an `InvoiceProcess` request over mutual TLS and polls until terminal. The lifecycle:

```text
Queued → Processing → (broker dispatches to Elsa → broker-dispatch runs the target workflow)
        → callback finalizes → Completed | Faulted
```

## Generate a migration

Migrations are scaffolded against the design-time factory, so no host needs to boot:

```bash
dotnet ef migrations add <Name> --project ElsaBroker.Data
```

## Stopping the lab (graceful shutdown)

The .NET broker services run on the Generic Host, so **Ctrl+C** in each `dotnet run` terminal triggers a
graceful shutdown — in-flight requests finish, the MassTransit bus and EF connections close, and the
listeners are released. Stop them in this order (loosely; it just avoids "connection refused" log noise):

1. **Processor** — Ctrl+C (stops consuming + dispatching to Elsa).
2. **Queue API** — Ctrl+C (releases the mTLS `:5001` and callback `:5080` listeners).

Then stop the containers (SQL Server, Elsa server, Elsa Studio — all three are managed by compose):

```bash
docker compose down          # graceful (SIGTERM → SIGKILL after grace); keeps the SQL data volume
docker compose down -v       # also drop the SQL data volume (fresh DB next run)
```
