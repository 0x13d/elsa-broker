# pivlib Plugin — Reference Example

A working reference plugin for ElsaBroker demonstrating the
[ADR-0009 plugin contract](../../docs/adr/0009-plugin-system.md). Uses PIV-themed
request types (HSPD-12 / FIPS 201-3 domain) with stubbed workflow responses. External
teams (starting with `app.pivlib`) follow this example to build real plugins.

## What is a plugin?

ElsaBroker is domain-agnostic: it queues, dispatches, and audits typed requests, but the
processing logic lives in Elsa workflow definitions. A **plugin** is an external project
that supplies its own:

1. **Elsa workflow definitions** (JSON files with `customProperties.requestType`)
2. **Request-type configuration** (validation rules + authorized clients)
3. **mTLS client credentials** (cert + allowlist entry)
4. **Docker Compose overlay** (volume mounts + config for the broker deployment)

No broker source code changes are required. See ADR-0009 for the full rationale.

## Plugin contract checklist

| Artifact | Location | Purpose |
|----------|----------|---------|
| Workflow JSONs | `workflows/*.json` | Elsa 3 definitions with `customProperties.requestType` |
| Request type config | `config/requestTypes.*.json` | Validation rules: required keys, additional keys |
| Allowlist entry | `config/allowlist.*.json` | Template for `ClientAllowlist.json` integration |
| Compose overlay | `docker-compose.*.yml` | Volume mounts for Elsa server + Processor |
| Test client | `test-client/` | Smoke test to verify end-to-end flow |

## Step-by-step: create your first plugin

### 1. Generate client certificates

Use `ElsaBroker.CertTools` to generate a client cert for your plugin's identity. Run from
the Queue's certs directory:

```bash
export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"
cd src/services/ElsaBroker/ElsaBroker.Queue/certs
dotnet run --project ../../ElsaBroker.CertTools -- client IdmsEmulator
```

This produces `IdmsEmulator.pem`, `IdmsEmulator.key`, and prints the SHA-1 thumbprint.

### 2. Add the allowlist entry

Copy the thumbprint into the broker's `ClientAllowlist.json`:

```json
{
  "clientId": "IdmsEmulator",
  "thumbprint": "<thumbprint from step 1>",
  "description": "PIV IDMS Emulator (app.pivlib plugin)"
}
```

See `config/allowlist.pivlib.json` for the template.

### 3. Author workflow definitions

Create Elsa 3 workflow JSON files in your plugin's `workflows/` directory. Each
request-handling workflow must declare `customProperties.requestType` so the broker's
`WorkflowFolderScanner` discovers it.

Minimal template:

```json
{
  "id": "<name>-v1",
  "definitionId": "<name>",
  "name": "<Human Name>",
  "version": 1,
  "isLatest": true,
  "isPublished": true,
  "customProperties": { "requestType": "<RequestType>" },
  "variables": [],
  "root": {
    "id": "Flowchart1",
    "type": "Elsa.Flowchart",
    "version": 1,
    "activities": [
      {
        "id": "WriteLine1",
        "type": "Elsa.WriteLine",
        "version": 1,
        "text": {
          "typeName": "String",
          "expression": { "type": "Literal", "value": "<your message>" }
        }
      }
    ],
    "connections": []
  }
}
```

For real workflows, author them in **Elsa Studio** (`http://localhost:13000`) and export
the JSON back into your `workflows/` directory.

### 4. Define request types

Create a request-type config file listing validation rules for each workflow's request type.
Merge these entries into the broker's `requestTypes.json` at deployment time.

See `config/requestTypes.pivlib.json` for the four PIV types this example defines.

### 5. Create a Compose overlay

Write a `docker-compose.<plugin>.yml` that mounts your workflow directory into the Elsa
server and (when the Processor is containerized) adds it to `Workflows:Paths`:

```yaml
services:
  elsa-server:
    volumes:
      - ./your-plugin/workflows:/app/YourPluginWorkflows:ro

  # When the Processor is containerized:
  # processor:
  #   volumes:
  #     - ./your-plugin/workflows:/app/your-plugin-workflows:ro
  #   environment:
  #     - Workflows__Paths__1=/app/your-plugin-workflows
```

Launch with:

```bash
docker compose -f docker-compose.yml -f your-compose-overlay.yml up
```

## This example's request types

| Request Type | Definition ID | Required Keys | Description |
|-------------|--------------|---------------|-------------|
| `PivEnroll` | `piv-enroll` | EmployeeId, AgencyCode | PIV credential enrollment |
| `DcsaVetting` | `dcsa-vetting` | EmployeeId, InvestigationType | DCSA background investigation |
| `CmsPersonalization` | `cms-personalization` | EmployeeId, FascN, CardUuid | Card Management System personalization |
| `PacsProvisioning` | `pacs-provisioning` | FascN, FacilityCode | Physical access control provisioning |

All workflows return static stub responses (e.g., "enrollment accepted", "vetting approved").
Replace with real workflow logic authored in Elsa Studio.

## Testing your plugin

### Verify scanner discovery

The broker's `WorkflowFolderScanner` should discover all request types from your workflow
directory. The test suite includes a test that scans this example's `workflows/` folder:

```bash
export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"
cd src/services/ElsaBroker
dotnet test --filter "Scan_discovers_all_pivlib_plugin_request_types"
```

### Run the smoke test

With the full stack running (SQL Server, Queue, Processor, Elsa server with the plugin
overlay), run the mTLS smoke test:

```bash
python3 examples/pivlib-plugin/test-client/smoke-pivlib.py [certs-dir]
```

This submits all four PIV request types and polls until each reaches a terminal state.

## For pivlib's team

This reference plugin uses PIV-themed request types as stubs. To build the real
`app.pivlib` integration:

1. **Start from this example** — copy the directory structure and adapt the workflow
   definitions for real PIV issuance logic.
2. **Wire biometric processing** — your WASM modules (WSQ decode, INCITS 381/378/385,
   CBEFF) integrate as Elsa activities or as HTTP services called from workflows.
3. **Own your workflows** — author and refine in Elsa Studio, export JSON back to your
   plugin's `workflows/` directory. The broker team maintains the platform; your team
   maintains the domain workflows.
4. **Coordinate on request types** — request-type names must be globally unique across all
   plugins. Use a consistent prefix (e.g., `Piv*`) to avoid collisions.
5. **Cert lifecycle** — generate your own client cert via CertTools. The broker operator
   adds your thumbprint to the allowlist. Rotation = new cert + updated allowlist entry.
