# Elsa integration

elsa-broker is a **durable queueing front end for [Elsa 3](https://elsaworkflows.io) workflows**. The
broker owns ingress (mTLS), authorization, durable delivery, and the audit trail; **the processing logic
for each request type is an Elsa workflow** — versioned in git, authored visually in Elsa Studio, and
diagrammed with the sibling [`elsa-to-mermaid`](https://ariugwu.com/elsa).

There are no hand-written C# handlers. A request type is "implemented" by dropping a workflow JSON in the
shared folder.

## The shared workflows folder + convention

The `workflows/` folder (under `src/services/ElsaBroker/`) is the single source of truth, loaded by
**two** consumers:

- The **custom `ElsaBroker.WorkflowServer`** mounts it at `/app/Workflows` and auto-imports every JSON
  file as a workflow on startup via `WorkflowFolderImporter`. This is a key advantage over the official
  demo image, which is DB-backed and does not auto-load the folder. With the custom server, dropping a
  workflow JSON into the folder and restarting the container is all that is needed to register it in Elsa.
- The **broker** (`ElsaBroker.Processor`) scans the same folder on startup and auto-registers
  `requestType → workflowDefinitionId`.

Each request-handling workflow declares its broker request type with a custom property:

```json
{
  "definitionId": "invoice-process",
  "customProperties": { "requestType": "InvoiceProcess" },
  "root": { "...": "activity graph" }
}
```

`WorkflowFolderScanner` reads `definitionId` + `customProperties.requestType`; a file without a
`requestType` (e.g. the dispatcher) is ignored. The result populates `WorkflowRegistry`, and the
Processor registers one `ElsaDispatchHandler` per discovered request type. Add a request type = add a
file; no redeploy of handler code.

## Deployment shape (remote Elsa server)

The workflow runs in a **separate Elsa server**, not in-process — so it can be operated, versioned, and
scaled independently, and authored in Studio. The broker talks to it over two hops:

```text
client ──mTLS :5001──▶ Queue API ──outbox──▶ SQL transport ──▶ Processor
                            ▲                                      │ ElsaDispatchHandler (Deferred)
                            │ callback                             │ POST {definitionId, correlationId,
                  :5080 shared-secret                              │       message, callbackUrl, secret}
                            │                                      ▼
                       Queue (callback) ◀── SendHttpRequest ── Elsa "broker-dispatch" workflow
                            │                                      │ DispatchWorkflow(definitionId,
                            ▼                                      ▼   WaitForCompletion) → target workflow
                     RequestRecord → Completed/Faulted
```

## Async callback model

Workflows can be long-running (timers, human approval), so the broker does **not** block a consumer
waiting for a result:

1. The Processor sets `RequestRecord` → `Processing` and calls `ElsaDispatchHandler`.
2. The handler `POST`s `{ definitionId, correlationId, message, callbackUrl, callbackSecret }` to the
   Elsa **broker-dispatch** workflow and returns `RequestResult(Deferred: true)`. The consumer leaves
   the record in `Processing` (it does **not** finalize).
3. `broker-dispatch` acks `202`, runs the target workflow by `definitionId` (`DispatchWorkflow` with
   `WaitForCompletion`), then `SendHttpRequest`s the outcome to `callbackUrl`.
4. The broker's **callback endpoint** (`POST /internal/requests/{id}/result` on the internal
   listener `:5080`) validates the `X-Callback-Secret` and sets the record → `Completed`/`Faulted`.
5. The client's poll on `:5001` eventually returns the terminal status.

The callback uses a **shared secret**, not mTLS: the Elsa container can't easily present a client
certificate, and the callback is a server-to-server hop on the internal network. The mTLS ingress on
`:5001` is untouched. See [ADR-0006](../adr/0006-elsa-workflows-as-the-processing-model.md).

## The dispatcher workflow

`workflows/broker-dispatch.json` is the single entry workflow the broker calls. Its contract (request
body, callback shape) is documented in the workflows folder README. It is best **authored in Elsa
Studio** — hand-written Elsa JSON (input binding + expressions) is fragile — then exported back into the
folder. Because the broker only needs `definitionId` + `customProperties.requestType` to register,
editing the graph in Studio never breaks registration.

## The feedback loop

The Docker stack runs all three services (`sqlserver`, `elsa-server`, `elsa-studio`), so the authoring
loop is:

1. `docker compose up -d` — starts SQL Server, the custom `ElsaBroker.WorkflowServer` on port 13002, and
   Elsa Studio on port 13000 (see [Getting started](getting-started.md)).
2. Open Studio at **http://localhost:13000** (`admin` / `password`).
3. Build/refine `broker-dispatch` and your request-type workflows visually.
4. **Export** the JSON back into `workflows/`. Restart the `elsa-server` container to pick up new files
   (`docker compose restart elsa-server`); the custom server auto-loads the folder on startup. The broker
   re-registers on next start.

> Note: the custom server loads the folder at startup, not on file change. A container restart is
> required when new workflow files are exported. Running workflows are not interrupted by a restart.

## Example child workflow

A small invoice workflow — branch on an auto-approve limit, else route to a human `Await approval` step
before completing. The async approval step is exactly the durable behavior workflows give you for free.
The diagram is generated from the JSON by `elsa-to-mermaid` (`docs/scripts/render-diagram.sh`):

[!INCLUDE [Invoice workflow diagram](../elsa/invoice-process.mermaid.md)]

[!code-json[invoice-process.elsa.json](../elsa/invoice-process.elsa.json)]

## Why the correlation id matters

The broker passes `request.CorrelationId` as the workflow's correlation id, keeping the
[idempotency model](messaging-design.md) intact: a redelivered message resolves to the same workflow
instance instead of starting a duplicate — the same id the audit `RequestRecord` is keyed on.

## Integration gotchas

These issues were discovered during end-to-end integration and are recorded here to save future time.

### 1. `PostAsJsonAsync` chunked encoding

Elsa's `HttpEndpoint` activity may not handle chunked `Transfer-Encoding` correctly. When dispatching
from the Processor to the Elsa server, use `StringContent` with an explicit `Content-Type` header rather
than `HttpClient.PostAsJsonAsync`:

```csharp
// Avoid — may produce chunked Transfer-Encoding that Elsa's HttpEndpoint rejects:
await httpClient.PostAsJsonAsync(url, payload);

// Prefer — explicit content type, no chunked encoding:
var json = JsonSerializer.Serialize(payload);
var content = new StringContent(json, Encoding.UTF8, "application/json");
await httpClient.PostAsync(url, content);
```

### 2. JavaScript expression headers need parentheses

Elsa's JavaScript evaluator for `SendHttpRequest` headers requires object literals to be wrapped in
parentheses: `({...})`. Without the outer parens, the JavaScript parser treats the opening `{` as a
block statement rather than an object literal, and the expression evaluates to `undefined`.

Correct form in the `Headers` field of a `SendHttpRequest` activity:

```javascript
({ "X-Callback-Secret": secret })
```

Incorrect (silently produces no header value):

```javascript
{ "X-Callback-Secret": secret }
```

### 3. Demo image's built-in MassTransit breaks `WaitForCompletion`

The official `elsa-server-and-studio-v3` demo image registers MassTransit internally. This routes
`DispatchWorkflow` through the MassTransit dispatcher, which sends the dispatch command asynchronously
through the bus. As a result, `WaitForCompletion=true` does not work: the dispatched child workflow runs
asynchronously and never resumes the parent `broker-dispatch` workflow synchronously. The callback to the
Queue never fires, and the `RequestRecord` stays `Processing` indefinitely.

The custom `ElsaBroker.WorkflowServer` intentionally omits MassTransit. It uses the in-process
`DefaultWorkflowDispatcher`, which executes `DispatchWorkflow` synchronously within the same request,
making `WaitForCompletion=true` behave correctly. This is the primary reason the official demo image was
replaced. See [ADR-0006](../adr/0006-elsa-workflows-as-the-processing-model.md).
