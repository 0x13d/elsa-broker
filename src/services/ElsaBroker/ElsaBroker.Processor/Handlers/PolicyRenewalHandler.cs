using ElsaBroker.Abstractions;
using ElsaBroker.Contracts;

namespace ElsaBroker.Processor.Handlers;

/// <summary>Handles ClientB / PolicyRenewal. Replace the body with real logic.</summary>
public class PolicyRenewalHandler(ILogger<PolicyRenewalHandler> logger) : IRequestHandler
{
    public string RequestType => "PolicyRenewal";

    public async Task<RequestResult> HandleAsync(ISubmitRequest request, CancellationToken ct)
    {
        var policy = request.Keys.GetValueOrDefault("PolicyNumber", "(unknown)");
        logger.LogInformation("Renewing policy {Policy}", policy);
        await Task.Delay(100, ct);
        return new RequestResult(true, $"Policy {policy} renewed OK", null);
    }
}
