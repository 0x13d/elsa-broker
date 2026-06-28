using System.Text.Json;
using Microsoft.Extensions.Logging;
using ElsaBroker.Data.Entities;

namespace ElsaBroker.Data.Registry;

/// <summary>
/// Reads requestTypes.json from disk and merges with DB, then populates the registry.
/// </summary>
public class RegistryLoader(
    BrokerDbContext          db,
    RequestTypeRegistry      registry,
    ILogger<RegistryLoader>  logger)
{
    public async Task LoadAsync(string jsonFilePath, CancellationToken ct = default)
    {
        var jsonModels     = LoadFromJson(jsonFilePath);
        var dbDefinitions  = await LoadFromDbAsync(ct);
        registry.Load(jsonModels, dbDefinitions);
    }

    private List<RequestTypeModel> LoadFromJson(string path)
    {
        if (!File.Exists(path))
        {
            logger.LogWarning("requestTypes.json not found at {Path} — skipping JSON source.", path);
            return [];
        }

        using var stream = File.OpenRead(path);
        var doc = JsonSerializer.Deserialize<JsonRequestTypeFile>(stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        return doc?.RequestTypes?.Select(r => new RequestTypeModel(
            r.ClientId,
            r.RequestType,
            r.RequiredKeys ?? [],
            r.AllowAdditionalKeys,
            "json")).ToList() ?? [];
    }

    private async Task<List<RequestTypeDefinition>> LoadFromDbAsync(CancellationToken ct)
    {
        try
        {
            return await Task.FromResult(db.RequestTypeDefinitions.Where(d => d.IsActive).ToList());
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not read RequestTypeDefinitions from DB — DB source skipped.");
            return [];
        }
    }

    // ── JSON file shape ───────────────────────────────────────────────────────
    private record JsonRequestTypeFile(List<JsonRequestTypeEntry>? RequestTypes);
    private record JsonRequestTypeEntry(
        string   ClientId,
        string   RequestType,
        string[] RequiredKeys,
        bool     AllowAdditionalKeys);
}
