using System.Text.Json;
using ElsaBroker.Abstractions;

namespace ElsaBroker.Switchboard.Data;

/// <summary>
/// <see cref="ILogShipper"/> implementation that writes broker events to the
/// Switchboard SQLite database. Uses <see cref="IServiceScopeFactory"/> to create
/// a scoped <see cref="SwitchboardDbContext"/> per write for thread safety.
/// </summary>
public class SwitchboardLogShipper : ILogShipper
{
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>
    /// Initializes a new instance of <see cref="SwitchboardLogShipper"/>.
    /// </summary>
    public SwitchboardLogShipper(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    /// <inheritdoc />
    public async Task ShipAsync(BrokerLogEvent logEvent, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SwitchboardDbContext>();

        var record = new LogEventRecord
        {
            Timestamp = logEvent.Timestamp,
            EventType = logEvent.EventType,
            Level = logEvent.Level,
            Source = logEvent.Source,
            CorrelationId = logEvent.CorrelationId,
            ClientId = logEvent.ClientId,
            RequestType = logEvent.RequestType,
            Message = logEvent.Message,
            PropertiesJson = logEvent.Properties is { Count: > 0 }
                ? JsonSerializer.Serialize(logEvent.Properties)
                : null,
            Exception = logEvent.Exception
        };

        db.LogEvents.Add(record);
        await db.SaveChangesAsync(ct);
    }
}
