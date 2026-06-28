namespace ElsaBroker.Data.Registry;

/// <summary>
/// Unified in-memory representation — source is either JSON or DB.
/// </summary>
public record RequestTypeModel(
    string   ClientId,
    string   RequestType,
    string[] RequiredKeys,
    bool     AllowAdditionalKeys,
    string   Source            // "json" | "database"
);
