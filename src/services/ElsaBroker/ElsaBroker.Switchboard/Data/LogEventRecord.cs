namespace ElsaBroker.Switchboard.Data;

/// <summary>
/// Persistent entity representing a single broker log event stored in SQLite.
/// Mapped from <see cref="Abstractions.BrokerLogEvent"/> by the log shipper.
/// </summary>
public class LogEventRecord
{
    /// <summary>Auto-incremented primary key.</summary>
    public int Id { get; set; }

    /// <summary>When the event occurred.</summary>
    public DateTimeOffset Timestamp { get; set; }

    /// <summary>Domain event type (e.g. RequestSubmitted, RequestCompleted).</summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>Log level: Information, Warning, Error.</summary>
    public string Level { get; set; } = string.Empty;

    /// <summary>Originating service: Queue, Processor, WorkflowServer.</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>Request correlation id.</summary>
    public string? CorrelationId { get; set; }

    /// <summary>Authenticated client id.</summary>
    public string? ClientId { get; set; }

    /// <summary>Request type (e.g. InvoiceProcess).</summary>
    public string? RequestType { get; set; }

    /// <summary>Human-readable description of the event.</summary>
    public string? Message { get; set; }

    /// <summary>JSON-serialized additional properties.</summary>
    public string? PropertiesJson { get; set; }

    /// <summary>Exception detail, if the event represents a fault.</summary>
    public string? Exception { get; set; }
}
