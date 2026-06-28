namespace ElsaBroker.Data.Registry;

/// <summary>
/// In-memory map of broker request type → Elsa workflow definition id, built from the shared
/// workflows folder by <see cref="WorkflowFolderScanner"/>. Lookups are case-insensitive.
/// </summary>
public class WorkflowRegistry
{
    private readonly Dictionary<string, WorkflowRegistration> _byRequestType;

    public WorkflowRegistry(IEnumerable<WorkflowRegistration> registrations)
    {
        _byRequestType = registrations.ToDictionary(
            r => r.RequestType.ToLowerInvariant(),
            r => r);
    }

    /// <summary>The Elsa workflow definition id that handles this request type, or null.</summary>
    public string? ResolveDefinitionId(string requestType) =>
        _byRequestType.TryGetValue(requestType.ToLowerInvariant(), out var r) ? r.WorkflowDefinitionId : null;

    public IReadOnlyCollection<WorkflowRegistration> All => _byRequestType.Values;
}
