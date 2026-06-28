using System.Text;
using System.Text.Json;
using ElsaBroker.Abstractions;
using Microsoft.Extensions.Logging;

namespace ElsaBroker.Data.Logging;

/// <summary>
/// Ships each event as a JSON POST to a configurable HTTP endpoint. Works with Seq
/// (<c>/api/events/raw</c>), Logstash (HTTP input), OpenSearch, or any JSON-accepting sink.
/// Uses a long-lived <see cref="HttpClient"/> created from the options; safe for singleton registration.
/// </summary>
public sealed class HttpJsonLogShipper : ILogShipper, IDisposable
{
    private readonly HttpClient _http = new();
    private readonly HttpJsonLogShipperOptions _options;
    private readonly ILogger<HttpJsonLogShipper> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public HttpJsonLogShipper(HttpJsonLogShipperOptions options, ILogger<HttpJsonLogShipper> logger)
    {
        _options = options;
        _logger  = logger;
        _http.BaseAddress = new Uri(options.SinkUrl);
    }

    public async Task ShipAsync(BrokerLogEvent logEvent, CancellationToken ct = default)
    {
        try
        {
            var json    = JsonSerializer.Serialize(logEvent, JsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            if (!string.IsNullOrEmpty(_options.ApiKey))
                content.Headers.Add("X-Seq-ApiKey", _options.ApiKey);

            await _http.PostAsync("", content, ct);
        }
        catch (Exception ex)
        {
            // Log shipping must never take down the request pipeline.
            _logger.LogWarning(ex, "Failed to ship log event to {SinkUrl}", _options.SinkUrl);
        }
    }

    public void Dispose() => _http.Dispose();
}

/// <summary>Configuration for <see cref="HttpJsonLogShipper"/>.</summary>
public sealed class HttpJsonLogShipperOptions
{
    /// <summary>HTTP endpoint that accepts JSON events (e.g. <c>http://seq:5341/api/events/raw</c>).</summary>
    public string SinkUrl { get; set; } = "http://localhost:5341/api/events/raw";

    /// <summary>Optional API key sent as <c>X-Seq-ApiKey</c> (Seq convention; ignored by other sinks).</summary>
    public string? ApiKey { get; set; }
}
