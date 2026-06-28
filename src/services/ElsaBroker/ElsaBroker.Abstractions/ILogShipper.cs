namespace ElsaBroker.Abstractions;

/// <summary>
/// Ships structured broker events to an external sink (console, HTTP/JSON endpoint, Seq, ELK, etc.).
/// Implement this interface to add a custom sink; register via DI in the host's
/// <c>Program.cs</c>.
/// </summary>
public interface ILogShipper
{
    /// <summary>Ship a single structured event to the sink.</summary>
    Task ShipAsync(BrokerLogEvent logEvent, CancellationToken ct = default);
}
