using System.Text.Json;
using ElsaBroker.Abstractions;

namespace ElsaBroker.Data.Logging;

/// <summary>
/// Writes each event as a single-line JSON object to stdout. Useful for development and for
/// container environments where a log collector (Fluentd, Promtail) scrapes stdout.
/// </summary>
public sealed class ConsoleLogShipper : ILogShipper
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public Task ShipAsync(BrokerLogEvent logEvent, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(logEvent, JsonOptions);
        Console.WriteLine(json);
        return Task.CompletedTask;
    }
}
