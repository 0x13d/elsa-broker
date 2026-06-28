using Elsa.Workflows;
using Elsa.Workflows.Management;
using Elsa.Workflows.Management.Models;

namespace ElsaBroker.WorkflowServer;

/// <summary>
/// Seeds the Elsa server from the shared <c>Workflows/</c> folder on startup: each JSON definition is
/// deserialized and imported (published) via Elsa's own importer, so the git folder is the source of
/// truth. This is the custom-server counterpart to the broker's <c>WorkflowFolderScanner</c>.
/// </summary>
public sealed class WorkflowFolderImporter(
    IServiceProvider                    services,
    IConfiguration                      configuration,
    ILogger<WorkflowFolderImporter>     logger) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        // Works for both `dotnet run` (bin/.../Workflows via Content copy) and the
        // container (/app/Workflows, mounted). Override with Elsa:WorkflowsPath.
        var dir = configuration["Elsa:WorkflowsPath"];
        if (string.IsNullOrWhiteSpace(dir))
            dir = Path.Combine(AppContext.BaseDirectory, "Workflows");

        if (!Directory.Exists(dir))
        {
            logger.LogWarning("Workflows folder not found at {Dir} — nothing to import.", dir);
            return;
        }

        using var scope = services.CreateScope();
        var importer   = scope.ServiceProvider.GetRequiredService<IWorkflowDefinitionImporter>();
        var serializer = scope.ServiceProvider.GetRequiredService<IApiSerializer>();

        var count = 0;
        foreach (var file in Directory.EnumerateFiles(dir, "*.json"))
        {
            try
            {
                var json  = await File.ReadAllTextAsync(file, ct);
                var model = serializer.Deserialize<WorkflowDefinitionModel>(json);
                await importer.ImportAsync(new SaveWorkflowDefinitionRequest { Model = model, Publish = true }, ct);
                logger.LogInformation("Imported workflow '{DefinitionId}' from {File}", model.DefinitionId, Path.GetFileName(file));
                count++;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to import workflow from {File}", file);
            }
        }

        logger.LogInformation("Workflow folder import complete: {Count} definition(s) from {Dir}", count, dir);
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
