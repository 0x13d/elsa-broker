using System.Text.Json;

namespace ElsaBroker.Data.Entities;

/// <summary>
/// Audit record written on receipt and updated on completion.
/// CorrelationId matches the MassTransit saga CorrelationId.
/// </summary>
public class RequestRecord
{
    public Guid   CorrelationId { get; set; }
    public string ClientId      { get; set; } = default!;
    public string RequestType   { get; set; } = default!;

    // Serialised Dictionary<string,string> — preserves key field data without
    // requiring a column per field type.
    public string KeysJson      { get; set; } = "{}";
    public string PayloadJson   { get; set; } = "{}";

    public string  Status      { get; set; } = default!;
    public string? Result      { get; set; }   // populated by Processor on completion
    public string? ErrorDetail { get; set; }   // populated on fault

    public DateTime SubmittedAt  { get; set; }
    public DateTime UpdatedAt    { get; set; }

    // ── Convenience helpers (not mapped) ─────────────────────────────────────
    public Dictionary<string, string> GetKeys() =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(KeysJson) ?? new();

    public void SetKeys(Dictionary<string, string> keys) =>
        KeysJson = JsonSerializer.Serialize(keys);
}
