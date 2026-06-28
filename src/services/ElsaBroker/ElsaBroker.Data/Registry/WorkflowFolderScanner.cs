using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace ElsaBroker.Data.Registry;

/// <summary>A request type discovered from an Elsa workflow definition file.</summary>
public record WorkflowRegistration(string RequestType, string WorkflowDefinitionId, string Name);

/// <summary>
/// Scans a folder of Elsa 3 workflow definition JSON files (the same folder the Elsa server loads)
/// and extracts the broker convention: a request-handling workflow declares
/// <c>customProperties.requestType</c>, and the broker dispatches that request type to the file's
/// <c>definitionId</c>. Files without a <c>requestType</c> (e.g. the dispatcher itself) are skipped.
/// </summary>
public static class WorkflowFolderScanner
{
    /// <summary>
    /// Scans multiple workflow directories and merges the results. On duplicate
    /// <c>requestType</c> across directories, the last directory wins (with a warning log).
    /// </summary>
    public static IReadOnlyList<WorkflowRegistration> Scan(IEnumerable<string> folderPaths, ILogger? logger = null)
    {
        var merged = new Dictionary<string, WorkflowRegistration>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in folderPaths)
        {
            foreach (var reg in Scan(folder))
            {
                if (merged.ContainsKey(reg.RequestType))
                    logger?.LogWarning("Duplicate requestType '{RequestType}' in {Folder} — overriding previous registration.", reg.RequestType, folder);
                merged[reg.RequestType] = reg;
            }
        }
        return merged.Values.ToList();
    }

    public static IReadOnlyList<WorkflowRegistration> Scan(string folderPath)
    {
        var results = new List<WorkflowRegistration>();
        if (!Directory.Exists(folderPath))
            return results;

        foreach (var file in Directory.EnumerateFiles(folderPath, "*.json", SearchOption.AllDirectories))
        {
            try
            {
                if (TryParse(File.ReadAllText(file)) is { } reg)
                    results.Add(reg);
            }
            catch (JsonException)
            {
                // Skip malformed JSON — a bad file shouldn't take down registration.
            }
        }
        return results;
    }

    /// <summary>Parse one definition's JSON; returns null if it is not a request-handling workflow.</summary>
    public static WorkflowRegistration? TryParse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            return null;

        var definitionId = GetStringCI(root, "definitionId");
        if (string.IsNullOrWhiteSpace(definitionId))
            return null;

        var requestType = GetCustomProperty(root, "requestType");
        if (string.IsNullOrWhiteSpace(requestType))
            return null; // not a request-handling workflow (e.g. the dispatcher)

        var name = GetStringCI(root, "name") ?? definitionId;
        return new WorkflowRegistration(requestType!, definitionId!, name);
    }

    private static string? GetStringCI(JsonElement obj, string name)
    {
        foreach (var p in obj.EnumerateObject())
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.String)
                return p.Value.GetString();
        return null;
    }

    private static string? GetCustomProperty(JsonElement root, string key)
    {
        foreach (var p in root.EnumerateObject())
        {
            if (!string.Equals(p.Name, "customProperties", StringComparison.OrdinalIgnoreCase)) continue;
            if (p.Value.ValueKind != JsonValueKind.Object) return null;
            return GetStringCI(p.Value, key);
        }
        return null;
    }
}
