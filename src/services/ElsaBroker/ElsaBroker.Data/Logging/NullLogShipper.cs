using ElsaBroker.Abstractions;

namespace ElsaBroker.Data.Logging;

/// <summary>
/// No-op shipper — discards all events. Registered by default when no sink is configured.
/// </summary>
public sealed class NullLogShipper : ILogShipper
{
    public Task ShipAsync(BrokerLogEvent logEvent, CancellationToken ct = default)
        => Task.CompletedTask;
}
