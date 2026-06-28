namespace ElsaBroker.Abstractions;

/// <summary>
/// A structured broker-domain event shipped to external sinks via <see cref="ILogShipper"/>.
/// Each event represents a meaningful state change in the request lifecycle (submitted, dispatched,
/// completed, faulted, etc.), not a raw log line.
/// </summary>
public sealed record BrokerLogEvent
{
    /// <summary>When the event occurred.</summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>
    /// Domain event type: <c>RequestSubmitted</c>, <c>RequestProcessing</c>,
    /// <c>RequestDispatched</c>, <c>RequestCompleted</c>, <c>RequestFaulted</c>,
    /// <c>CallbackReceived</c>, <c>WorkflowImported</c>, etc.
    /// </summary>
    public required string EventType { get; init; }

    /// <summary>Log level: Information, Warning, Error.</summary>
    public required string Level { get; init; }

    /// <summary>Originating service: Queue, Processor, WorkflowServer.</summary>
    public required string Source { get; init; }

    /// <summary>Human-readable description of the event.</summary>
    public string? Message { get; init; }

    /// <summary>Request correlation id (links the event to a <c>RequestRecord</c>).</summary>
    public string? CorrelationId { get; init; }

    /// <summary>Authenticated client id (from the mTLS certificate).</summary>
    public string? ClientId { get; init; }

    /// <summary>Request type (e.g. <c>InvoiceProcess</c>).</summary>
    public string? RequestType { get; init; }

    /// <summary>Additional structured properties specific to this event.</summary>
    public Dictionary<string, object?>? Properties { get; init; }

    /// <summary>Exception detail, if the event represents a fault.</summary>
    public string? Exception { get; init; }
}
