using System.Net.Http.Json;
using ElsaBroker.Abstractions;
using ElsaBroker.Contracts;

namespace ElsaBroker.Processor.Handlers;

/// <summary>
/// Handles a request type by dispatching it to an Elsa 3 workflow (the remote-server model). One
/// instance is registered per request type discovered in the shared workflows folder. It POSTs the
/// message to the Elsa "broker-dispatch" workflow and returns immediately with <c>Deferred = true</c>;
/// the workflow runs the target definition and calls the broker's callback endpoint to finalize.
/// </summary>
public sealed class ElsaDispatchHandler(
    string                          requestType,
    string                          workflowDefinitionId,
    HttpClient                      http,
    ElsaDispatchOptions             options,
    ILogger<ElsaDispatchHandler>    logger) : IRequestHandler
{
    public string RequestType => requestType;

    public async Task<RequestResult> HandleAsync(ISubmitRequest request, CancellationToken ct)
    {
        var callbackUrl = $"{options.CallbackBaseUrl.TrimEnd('/')}/internal/requests/{request.CorrelationId}/result";

        var body = new
        {
            definitionId   = workflowDefinitionId,
            correlationId  = request.CorrelationId,
            message        = new { keys = request.Keys, payload = request.Payload },
            callbackUrl,
            callbackSecret = options.CallbackSecret,
        };

        // Buffer to a StringContent so the request carries Content-Length. PostAsJsonAsync
        // streams chunked (no length), and Elsa's HttpEndpoint won't parse a chunked body —
        // its ParsedContent comes back null and the dispatcher's bindings see a null Request.
        var json    = System.Text.Json.JsonSerializer.Serialize(body);
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        var response = await http.PostAsync(options.DispatchUrl, content, ct);
        response.EnsureSuccessStatusCode();

        logger.LogInformation(
            "Dispatched {RequestType} [{CorrelationId}] to Elsa workflow '{DefinitionId}'; awaiting callback.",
            requestType, request.CorrelationId, workflowDefinitionId);

        // Terminal status arrives via the callback endpoint.
        return new RequestResult(Success: true, Output: null, ErrorDetail: null, Deferred: true);
    }
}

/// <summary>Configuration for the broker → Elsa dispatch + the callback the workflow posts back.</summary>
public sealed class ElsaDispatchOptions
{
    /// <summary>HTTP endpoint of the Elsa "broker-dispatch" workflow.</summary>
    public string DispatchUrl { get; set; } = "http://localhost:13000/workflows/broker-dispatch";

    /// <summary>Base URL the Elsa workflow uses to call back the broker's internal listener.</summary>
    public string CallbackBaseUrl { get; set; } = "http://localhost:5080";

    /// <summary>Shared secret sent as <c>X-Callback-Secret</c> and validated by the callback endpoint.</summary>
    public string CallbackSecret { get; set; } = "";

    /// <summary>Folder of Elsa workflow definitions (same folder the Elsa server loads).</summary>
    public string WorkflowsPath { get; set; } = "";
}
