namespace ElsaBroker.Data.Entities;

/// <summary>
/// Database-sourced request type definition.
/// Merges with requestTypes.json at startup; DB rows win on conflict.
/// </summary>
public class RequestTypeDefinition
{
    public int    Id                  { get; set; }
    public string ClientId            { get; set; } = default!;
    public string RequestType         { get; set; } = default!;

    // Comma-separated required key names — simple to edit in the DB directly.
    public string RequiredKeys        { get; set; } = string.Empty;
    public bool   AllowAdditionalKeys { get; set; }
    public bool   IsActive            { get; set; } = true;

    public DateTime CreatedAt         { get; set; }
    public DateTime UpdatedAt         { get; set; }

    public IEnumerable<string> GetRequiredKeys() =>
        RequiredKeys.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
