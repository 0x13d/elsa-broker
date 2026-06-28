---
status: accepted
date: 2026-06-09
decision-makers: dotnet-architect, docs-web
consulted: security, db-architect, project-manager
informed: team
---

# Plugin system for external workflow provisioning

## Context and Problem Statement

ElsaBroker is domain-agnostic: it queues, dispatches, and audits typed requests, but the *processing
logic* lives in Elsa workflow definitions (see [ADR-0006](0006-elsa-workflows-as-the-processing-model.md)).
External projects — starting with `app.pivlib` (IDMS Emulator / PIV issuance pipeline) — need to supply
their own workflow definitions, request-type configurations, and mTLS credentials to a broker deployment
**without modifying broker source code or rebuilding broker images**.

How should domain-specific plugins provision their workflows and configuration into an ElsaBroker
deployment?

## Decision Drivers

* **Domain agnosticism** — the broker must not contain domain-specific logic; plugins bring it.
* **Convention continuity** — the `customProperties.requestType` folder convention established in
  [ADR-0006](0006-elsa-workflows-as-the-processing-model.md) should be preserved, not replaced.
* **Docker Compose simplicity** — operators add a plugin by layering a Compose override file, not by
  rebuilding images or running import scripts.
* **Package boundary respect** — plugins reference `ElsaBroker.Contracts` (wire contract) or
  `ElsaBroker.Abstractions` (extensibility interfaces) per
  [ADR-0008](0008-nuget-package-boundaries.md), never internal broker types.
* **Credential isolation** — each plugin client gets its own cert and allowlist entry; no shared secrets
  beyond the Elsa callback secret.
* **Minimal broker code changes** — the plugin system should require the fewest possible changes to the
  broker itself.

## Considered Options

* **Option A:** Multi-path scanner (config-driven folder list)
* **Option B:** Single-folder volume-mount merge
* **Option C:** NuGet content files
* **Option D:** CLI import command

## Decision Outcome

Chosen option: **Option A (Multi-path scanner)**, because it requires one small, backward-compatible
code change (`WorkflowFolderScanner.Scan` accepts multiple directories), preserves the proven
folder-scanning convention from ADR-0006, and composes naturally with Docker volume mounts — each
plugin mounts its own directory into the container and adds it to the `Workflows:Paths` configuration
array.

### Plugin Contract

A conforming plugin provides:

1. **Workflow definitions** — Elsa 3 JSON files following the ADR-0006 convention:
   `customProperties.requestType` declares the request type handled by each workflow. Files live in
   the plugin's own directory (e.g. `pivlib/workflows/`).

2. **Request-type configuration** — entries for the broker's `requestTypes.json` (or a plugin-specific
   JSON file merged at startup) mapping request types to their validation rules, authorized clients,
   and metadata.

3. **Client certificate + allowlist entry** — generated via `ElsaBroker.CertTools`, added to the
   broker's `ClientAllowlist.json`. Each plugin client authenticates independently per
   [ADR-0003](0003-mtls-client-certificate-authorization.md).

4. **Compose overlay** — a `docker-compose.override.yml` (or named override) that:
   - Mounts the plugin's workflow directory into the Processor and Elsa Server containers.
   - Adds the plugin path to the `Workflows__Paths` environment variable array.
   - Mounts or merges any additional request-type config.

### Credential Provisioning

No changes to the security model. CertTools generates client certs; the operator adds thumbprints to
the allowlist; the Elsa callback shared secret is set via environment variable and shared between the
broker and Elsa server as today. Plugins do not introduce new authentication mechanisms.

### Compose Topology

```
# Base: docker-compose.yml (broker only)
services:
  processor:
    volumes:
      - ./workflows:/app/workflows

# Plugin override: docker-compose.pivlib.yml
services:
  processor:
    volumes:
      - ./pivlib/workflows:/app/pivlib-workflows
    environment:
      - Workflows__Paths__1=/app/pivlib-workflows
  elsa-server:
    volumes:
      - ./pivlib/workflows:/app/PivlibWorkflows
```

The Processor scans both `/app/workflows` (index 0, default) and `/app/pivlib-workflows` (index 1,
from the plugin override). The Elsa server loads all workflow files from its own mount points.

### Consequences

* Good, because adding a plugin is a Compose overlay + config change — no broker source edits, no
  image rebuilds.
* Good, because the existing single-folder convention is fully preserved as the default; the change
  is purely additive.
* Good, because multiple plugins compose independently — each mounts its own directory and adds its
  own config entry.
* Good, because credential isolation is maintained — each plugin client gets its own cert/allowlist
  entry.
* Bad, because on duplicate `requestType` across directories, the scanner must pick a winner
  (last-wins). Operators must ensure request-type names are globally unique across plugins.
* Neutral, because the Elsa server needs its own mount points for plugin workflow directories; the
  broker and Elsa server volume configs must stay in sync.

### Confirmation

1. `WorkflowFolderScannerTests` cover multi-path scanning: distinct directories, duplicate handling,
   empty paths, and mixed valid/nonexistent directories.
2. The Processor's `Program.cs` reads `Workflows:Paths` as a string array, falling back to the legacy
   `Elsa:WorkflowsPath` single-path config for backward compatibility.
3. `dotnet build` succeeds with zero warnings; `dotnet test` passes all existing and new tests.

## Pros and Cons of the Options

### Option A: Multi-path scanner (config-driven folder list) — RECOMMENDED

Extend `WorkflowFolderScanner.Scan` to accept multiple directories via a `Workflows:Paths`
configuration array. Default `["workflows"]` preserves backward compatibility. Each plugin mounts its
workflow directory and adds its path to the array.

* Good, because it requires minimal code change (one new method overload + startup config read).
* Good, because it preserves the ADR-0006 folder convention exactly — no new discovery mechanism.
* Good, because plugins are isolated: each has its own directory, its own cert, its own config entry.
* Good, because Docker Compose override files naturally layer additional volume mounts and env vars.
* Neutral, because operators must keep Processor and Elsa server volume mounts in sync.
* Bad, because duplicate `requestType` across directories requires a conflict-resolution policy
  (last-wins with a warning log).

### Option B: Single-folder volume-mount merge

All plugins mount their workflow files into the same `/app/workflows` directory. No code changes
needed — the existing scanner already picks up all JSON files recursively.

* Good, because it requires zero code changes to the broker.
* Bad, because **filename collisions** are likely when multiple plugins mount into the same directory
  — Docker volume mounts are not merge-aware; the last mount wins and shadows earlier files.
* Bad, because there is no way to attribute a workflow to its source plugin for debugging or auditing.
* Bad, because removing a plugin requires knowing exactly which files it contributed.

### Option C: NuGet content files

Plugins ship as NuGet packages with workflow JSON files as `contentFiles`. The Processor project
references the plugin package, and MSBuild copies the files into the output directory at build time.

* Good, because package versioning and dependency resolution are handled by NuGet.
* Bad, because it **couples plugin delivery to the build pipeline** — adding a plugin requires
  modifying a `.csproj`, running `dotnet restore`, and rebuilding the Processor image.
* Bad, because it violates the "no broker source changes" principle — the Processor project must
  reference each plugin package.
* Bad, because Docker Compose operators cannot add a plugin at deployment time without rebuilding.

### Option D: CLI import command

A `dotnet elsa-broker import-plugin <path>` command copies workflow files into the broker's workflow
directory and merges request-type config.

* Good, because it provides explicit control over what gets imported.
* Bad, because it introduces a **stateful import step** that must be re-run on every deployment,
  complicating CI/CD and Docker workflows.
* Bad, because it conflates deployment-time provisioning with a one-shot CLI tool, creating drift
  between the plugin source and the broker's imported copy.
* Bad, because it requires writing and maintaining a new CLI command in the broker.

## More Information

* This ADR is the key deliverable of backlog item **EB-007a** and gates the four implementation
  sub-cards (EB-007b through EB-007e).
* The first plugin consumer is `app.pivlib` (IDMS Emulator / PIV issuance pipeline). Its Compose
  overlay will mount PIV-specific workflows and register request types like `PivIssuance`,
  `PivRenewal`, etc.
* The `Workflows:Paths` config array maps naturally to .NET's array configuration binding:
  `Workflows__Paths__0=/app/workflows`, `Workflows__Paths__1=/app/pivlib-workflows`, etc.
* Future enhancement: a startup log that lists all scanned directories and discovered request types,
  aiding operators in verifying plugin registration.
* Related ADRs: [ADR-0003](0003-mtls-client-certificate-authorization.md) (mTLS model),
  [ADR-0004](0004-config-driven-request-type-registry.md) (request-type registry),
  [ADR-0006](0006-elsa-workflows-as-the-processing-model.md) (workflow convention),
  [ADR-0008](0008-nuget-package-boundaries.md) (package boundaries).
