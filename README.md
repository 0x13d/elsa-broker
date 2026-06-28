# elsa-broker

A durable, **mTLS-secured queueing front end for [Elsa 3](https://elsaworkflows.io) workflows**. Clients
submit typed requests over an HTTP API authenticated by client certificates; the broker delivers them
durably and **dispatches each request type to an Elsa workflow** for the actual work. Workflows are
versioned in git, authored visually in Elsa Studio, and added by dropping a JSON file in a shared folder
— no handler code. Built for **resiliency** (transactional outbox/inbox, async callbacks) and **trust**
(authorization asserted by the certificate, never the caller).

> Part of [ariugwu.com](https://ariugwu.com) · project page: <https://ariugwu.com/elsa-broker>

## What it is

- **Queue API** (`ElsaBroker.Queue`) — ASP.NET Core. mTLS ingress (`:5001`): validates the request type
  against a per-client registry, enqueues an `ISubmitRequest`, and serves status polls. Also hosts the
  internal callback listener (`:5080`) that finalizes a request when its workflow completes.
- **Processor** (`ElsaBroker.Processor`) — a worker that consumes requests and **dispatches each request
  type to its Elsa workflow** (async), leaving the audit record `Processing` until the callback.
- **Elsa 3 server + Studio** (Docker) — runs the workflows; loads them from the shared `workflows/`
  folder. Studio is the author-and-export-back loop.
- **CertTools** (`ElsaBroker.CertTools`) — a CLI that mints the internal CA plus server and client certs.

A request type is "implemented" by a workflow JSON in `workflows/` declaring
`customProperties.requestType`; the broker auto-registers `requestType → definitionId`.

## Stack

- .NET 9 · MassTransit 8.3 (SQL Server transport) · EF Core 9 + SQL Server · mTLS (Kestrel + internal CA)
- **Elsa 3** Server + Studio (Docker); workflow diagrams via the sibling `elsa-to-mermaid`
- Docs: a **DocFX** site (API reference, conceptual articles, ADRs, deployment diagram, test results)
  published to `dist/elsa-broker`.

## Getting started

> The .NET 9 SDK is installed under `~/.dotnet` (the system SDK is 8.0). Put it on PATH first.

```bash
export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"
export DOTNET_ROOT="$HOME/.dotnet"
cd src/services/ElsaBroker

docker compose up -d        # SQL Server + Elsa Server/Studio (Studio at http://localhost:13000)
dotnet build                # builds all projects
dotnet test                 # xUnit + coverage
```

The full lab walkthrough — Studio login, certificates, running the services, and an end-to-end request
through Elsa — is in the [getting-started guide](docs/articles/getting-started.md) and
[src/services/ElsaBroker/README.md](src/services/ElsaBroker/README.md).

## Documentation

The DocFX site (under `docs/`) is the project's documentation **and** its landing page: the
[Elsa integration](docs/articles/elsa-integration.md) model, the
[deployment diagram](docs/articles/elsa-deployment.md), the security model,
[Architectural Decision Records](docs/adr/), generated API reference, and browsable test/coverage
reports. Diagrams render offline.

## Repository layout

```text
src/services/ElsaBroker/             the .NET solution (see its README for details)
src/services/ElsaBroker/workflows/   shared Elsa 3 workflow definitions (dispatcher + per request type)
docs/                                DocFX site sources (articles, ADRs, diagrams, test results)
.claude/agents/                      the project team (architect, db, security, test, docs/web)
```

## Privacy

ElsaBroker contains **no telemetry**. Its only network dependencies are the SQL Server it is configured
against and the message bus. Documentation diagrams are rendered offline — no third-party diagram
services are contacted.
