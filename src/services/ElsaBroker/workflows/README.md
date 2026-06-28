# workflows/ — shared Elsa 3 workflow definitions

This folder is the **single source of truth** for the request types elsa-broker can handle. It is loaded
by **two** consumers:

1. **The Elsa server** (Docker) mounts it at `/app/Workflows`, so every JSON file here becomes a
   published Elsa workflow definition.
2. **The broker** (`ElsaBroker.Processor`) scans the same folder on startup and **auto-registers**
   `requestType → workflowDefinitionId` from each file (see `WorkflowFolderScanner`).

## Convention

Each **request-handling** workflow declares its broker request type via a custom property:

```json
{
  "definitionId": "invoice-process",
  "name": "Invoice Process",
  "isLatest": true,
  "isPublished": true,
  "customProperties": { "requestType": "InvoiceProcess" },
  "root": { "...": "activity graph" }
}
```

- `definitionId` — the Elsa workflow id the broker dispatches to.
- `customProperties.requestType` — the broker request type this workflow handles. A file **without**
  this property is ignored by the broker's auto-registration (e.g. the dispatcher below).
- The `requestType` should also be authorized in `ElsaBroker.Queue/requestTypes.json` (which client may
  submit it + required keys).

## The dispatcher

`broker-dispatch.json` is the single entry workflow the broker calls. Contract (HTTP endpoint
`POST /workflows/broker-dispatch`), JSON body:

```json
{
  "definitionId": "invoice-process",
  "correlationId": "<guid>",
  "message": { "keys": { "...": "..." }, "payload": { "...": "..." } },
  "callbackUrl": "http://host.docker.internal:5080/internal/requests/<guid>/result",
  "callbackSecret": "<shared secret>"
}
```

It must: respond `202` immediately, run the target workflow (`DispatchWorkflow`, `WaitForCompletion`)
with `message` as input, then `SendHttpRequest` the outcome to `callbackUrl` with header
`X-Callback-Secret: <callbackSecret>` and body `{ "status": "Completed|Faulted", "result": "...",
"error": "..." }`.

> **Author/refine in Elsa Studio.** Hand-written Elsa JSON (input binding + expressions) is fragile;
> the JSON here is a **starter**. Build it visually in Studio (in the Docker stack), then export it back
> into this folder — that's the intended feedback loop. The broker only needs `definitionId` +
> `customProperties.requestType` to register, so editing in Studio won't break registration.
