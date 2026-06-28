using ElsaBroker.Abstractions;
using ElsaBroker.Contracts;

namespace ElsaBroker.Processor.Handlers;

/// <summary>Handles ClientA / InvoiceProcess. Replace the body with real logic.</summary>
public class InvoiceProcessHandler(ILogger<InvoiceProcessHandler> logger) : IRequestHandler
{
    public string RequestType => "InvoiceProcess";

    public async Task<RequestResult> HandleAsync(ISubmitRequest request, CancellationToken ct)
    {
        var invoice = request.Keys.GetValueOrDefault("InvoiceNumber", "(unknown)");
        var vendor  = request.Keys.GetValueOrDefault("VendorCode",    "(unknown)");
        logger.LogInformation("Processing invoice {Invoice} for vendor {Vendor}", invoice, vendor);
        await Task.Delay(100, ct); // simulate work
        return new RequestResult(true, $"Invoice {invoice} processed OK", null);
    }
}
