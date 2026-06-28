using ElsaBroker.Contracts;

namespace ElsaBroker.Abstractions;

/// <summary>
/// Handles a specific request type. Implementations are resolved by
/// <c>SubmitRequestConsumer</c> via the DI container, matched on
/// <see cref="RequestType"/> (case-insensitive).
/// </summary>
public interface IRequestHandler
{
    /// <summary>Matches the RequestType value -- case-insensitive.</summary>
    string RequestType { get; }

    /// <summary>Process the request and return a result.</summary>
    Task<RequestResult> HandleAsync(ISubmitRequest request, CancellationToken ct);
}

/// <summary>
/// Outcome of a handler. <see cref="Deferred"/> means the handler accepted the work but the
/// terminal status will arrive later via an out-of-band callback (the async Elsa model) -- the consumer
/// should leave the record in <c>Processing</c> rather than finalizing it.
/// </summary>
public record RequestResult(bool Success, string? Output, string? ErrorDetail, bool Deferred = false);
