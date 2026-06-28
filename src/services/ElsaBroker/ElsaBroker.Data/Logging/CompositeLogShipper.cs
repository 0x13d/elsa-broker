using ElsaBroker.Abstractions;

namespace ElsaBroker.Data.Logging;

/// <summary>
/// Fans out each event to multiple shippers. Failures in one shipper do not prevent delivery
/// to the others.
/// </summary>
public sealed class CompositeLogShipper(IEnumerable<ILogShipper> shippers) : ILogShipper
{
    private readonly ILogShipper[] _shippers = shippers.ToArray();

    public async Task ShipAsync(BrokerLogEvent logEvent, CancellationToken ct = default)
    {
        foreach (var shipper in _shippers)
        {
            try
            {
                await shipper.ShipAsync(logEvent, ct);
            }
            catch
            {
                // Best-effort: one sink failing must not block the others.
            }
        }
    }
}
