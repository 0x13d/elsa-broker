using Microsoft.Extensions.Logging;
using ElsaBroker.Data.Entities;

namespace ElsaBroker.Data.Registry;

/// <summary>
/// Merges requestTypes.json (static/ours) with DB rows (dynamic/client).
/// DB rows win on conflict so clients can override defaults without a redeploy.
/// Call RefreshAsync() to hot-reload from the DB at runtime.
/// </summary>
public class RequestTypeRegistry(ILogger<RequestTypeRegistry> logger)
{
    // Key: (clientId, requestType) — case-insensitive
    private Dictionary<(string, string), RequestTypeModel> _map = new();

    public void Load(
        IEnumerable<RequestTypeModel>      jsonModels,
        IEnumerable<RequestTypeDefinition> dbDefinitions)
    {
        var map = new Dictionary<(string, string), RequestTypeModel>(
            StringComparer.OrdinalIgnoreCase.Equals(null!, null!) // dummy, key is ValueTuple
                ? EqualityComparer<(string, string)>.Default
                : new TupleIgnoreCaseComparer());

        // 1. Seed from JSON
        foreach (var m in jsonModels)
        {
            var key = (m.ClientId.ToLowerInvariant(), m.RequestType.ToLowerInvariant());
            map[key] = m;
            logger.LogInformation("Registry: loaded [{Source}] {ClientId}/{RequestType}", m.Source, m.ClientId, m.RequestType);
        }

        // 2. DB rows overlay (and override) JSON entries
        foreach (var d in dbDefinitions.Where(d => d.IsActive))
        {
            var key   = (d.ClientId.ToLowerInvariant(), d.RequestType.ToLowerInvariant());
            var model = new RequestTypeModel(
                d.ClientId, d.RequestType,
                d.GetRequiredKeys().ToArray(),
                d.AllowAdditionalKeys,
                "database");
            map[key] = model;
            logger.LogInformation("Registry: loaded [database] {ClientId}/{RequestType}", d.ClientId, d.RequestType);
        }

        _map = map;
        logger.LogInformation("Registry: {Count} request types active.", _map.Count);
    }

    public RequestTypeModel? Get(string clientId, string requestType)
    {
        var key = (clientId.ToLowerInvariant(), requestType.ToLowerInvariant());
        return _map.TryGetValue(key, out var m) ? m : null;
    }

    public IReadOnlyCollection<RequestTypeModel> All() => _map.Values;
}

// ValueTuple comparer that ignores case on both elements
file sealed class TupleIgnoreCaseComparer : IEqualityComparer<(string, string)>
{
    public bool Equals((string, string) x, (string, string) y) =>
        string.Equals(x.Item1, y.Item1, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(x.Item2, y.Item2, StringComparison.OrdinalIgnoreCase);

    public int GetHashCode((string, string) obj) =>
        HashCode.Combine(
            obj.Item1.ToLowerInvariant().GetHashCode(),
            obj.Item2.ToLowerInvariant().GetHashCode());
}
