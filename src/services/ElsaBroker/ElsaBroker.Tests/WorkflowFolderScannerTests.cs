using ElsaBroker.Data.Registry;
using Xunit;

namespace ElsaBroker.Tests;

public class WorkflowFolderScannerTests
{
    private const string HandlerJson = """
        {
          "definitionId": "invoice-process",
          "name": "Invoice Process",
          "isPublished": true,
          "customProperties": { "requestType": "InvoiceProcess" },
          "root": { "type": "Elsa.Flowchart", "activities": [] }
        }
        """;

    private const string PivIssuanceJson = """
        {
          "definitionId": "piv-issuance",
          "name": "PIV Issuance",
          "isPublished": true,
          "customProperties": { "requestType": "PivIssuance" },
          "root": { "type": "Elsa.Flowchart", "activities": [] }
        }
        """;

    private const string InvoiceProcessOverrideJson = """
        {
          "definitionId": "invoice-process-v2",
          "name": "Invoice Process V2",
          "isPublished": true,
          "customProperties": { "requestType": "InvoiceProcess" },
          "root": { "type": "Elsa.Flowchart", "activities": [] }
        }
        """;

    private const string DispatcherJson = """
        {
          "definitionId": "broker-dispatch",
          "name": "Broker Dispatch",
          "customProperties": { "role": "dispatcher" },
          "root": { "type": "Elsa.Flowchart", "activities": [] }
        }
        """;

    [Fact]
    public void TryParse_extracts_requestType_and_definitionId()
    {
        var reg = WorkflowFolderScanner.TryParse(HandlerJson);

        Assert.NotNull(reg);
        Assert.Equal("InvoiceProcess", reg!.RequestType);
        Assert.Equal("invoice-process", reg.WorkflowDefinitionId);
        Assert.Equal("Invoice Process", reg.Name);
    }

    [Fact]
    public void TryParse_skips_workflows_without_requestType()
    {
        Assert.Null(WorkflowFolderScanner.TryParse(DispatcherJson));
    }

    [Fact]
    public void TryParse_returns_null_when_definitionId_missing()
    {
        Assert.Null(WorkflowFolderScanner.TryParse("""{ "customProperties": { "requestType": "X" } }"""));
    }

    [Fact]
    public void Scan_registers_handlers_and_skips_dispatcher_and_malformed()
    {
        var dir = Path.Combine(Path.GetTempPath(), "wf-scan-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "invoice.json"), HandlerJson);
            File.WriteAllText(Path.Combine(dir, "dispatch.json"), DispatcherJson);
            File.WriteAllText(Path.Combine(dir, "broken.json"), "{ not valid json ");

            var regs = WorkflowFolderScanner.Scan(dir);

            Assert.Single(regs);
            Assert.Equal("InvoiceProcess", regs[0].RequestType);

            var registry = new WorkflowRegistry(regs);
            Assert.Equal("invoice-process", registry.ResolveDefinitionId("invoiceprocess")); // case-insensitive
            Assert.Null(registry.ResolveDefinitionId("Unknown"));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // ── Multi-path Scan overload tests ──────────────────────────────────────

    [Fact]
    public void Scan_multi_path_merges_distinct_workflows_from_two_directories()
    {
        var dir1 = Path.Combine(Path.GetTempPath(), "wf-multi1-" + Guid.NewGuid());
        var dir2 = Path.Combine(Path.GetTempPath(), "wf-multi2-" + Guid.NewGuid());
        Directory.CreateDirectory(dir1);
        Directory.CreateDirectory(dir2);
        try
        {
            File.WriteAllText(Path.Combine(dir1, "invoice.json"), HandlerJson);
            File.WriteAllText(Path.Combine(dir2, "piv.json"), PivIssuanceJson);

            var regs = WorkflowFolderScanner.Scan(new[] { dir1, dir2 });

            Assert.Equal(2, regs.Count);
            Assert.Contains(regs, r => r.RequestType == "InvoiceProcess");
            Assert.Contains(regs, r => r.RequestType == "PivIssuance");
        }
        finally
        {
            Directory.Delete(dir1, recursive: true);
            Directory.Delete(dir2, recursive: true);
        }
    }

    [Fact]
    public void Scan_multi_path_duplicate_requestType_last_wins()
    {
        var dir1 = Path.Combine(Path.GetTempPath(), "wf-dup1-" + Guid.NewGuid());
        var dir2 = Path.Combine(Path.GetTempPath(), "wf-dup2-" + Guid.NewGuid());
        Directory.CreateDirectory(dir1);
        Directory.CreateDirectory(dir2);
        try
        {
            File.WriteAllText(Path.Combine(dir1, "invoice.json"), HandlerJson);
            File.WriteAllText(Path.Combine(dir2, "invoice-v2.json"), InvoiceProcessOverrideJson);

            var regs = WorkflowFolderScanner.Scan(new[] { dir1, dir2 });

            Assert.Single(regs);
            Assert.Equal("invoice-process-v2", regs[0].WorkflowDefinitionId);
        }
        finally
        {
            Directory.Delete(dir1, recursive: true);
            Directory.Delete(dir2, recursive: true);
        }
    }

    [Fact]
    public void Scan_multi_path_empty_array_returns_empty()
    {
        var regs = WorkflowFolderScanner.Scan(Array.Empty<string>());

        Assert.Empty(regs);
    }

    [Fact]
    public void Scan_multi_path_skips_nonexistent_directory()
    {
        var dir1 = Path.Combine(Path.GetTempPath(), "wf-valid-" + Guid.NewGuid());
        var dir2 = Path.Combine(Path.GetTempPath(), "wf-nonexistent-" + Guid.NewGuid());
        Directory.CreateDirectory(dir1);
        try
        {
            File.WriteAllText(Path.Combine(dir1, "invoice.json"), HandlerJson);

            var regs = WorkflowFolderScanner.Scan(new[] { dir1, dir2 });

            Assert.Single(regs);
            Assert.Equal("InvoiceProcess", regs[0].RequestType);
        }
        finally { Directory.Delete(dir1, recursive: true); }
    }

    // ── Reference plugin validation ──────────────────────────────────────

    /// <summary>
    /// Scans the actual examples/pivlib-plugin/workflows/ directory and verifies
    /// all four PIV request types are well-formed and discoverable.
    /// </summary>
    [Fact]
    public void Scan_discovers_all_pivlib_plugin_request_types()
    {
        // Walk up from the test assembly's output directory to the solution root,
        // then into the examples/pivlib-plugin/workflows folder.
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "ElsaBroker.sln")))
            dir = Directory.GetParent(dir)?.FullName;
        Assert.NotNull(dir);

        var pluginWorkflows = Path.Combine(dir!, "..", "..", "..", "examples", "pivlib-plugin", "workflows");
        pluginWorkflows = Path.GetFullPath(pluginWorkflows);
        Assert.True(Directory.Exists(pluginWorkflows), $"Plugin workflows directory not found: {pluginWorkflows}");

        var regs = WorkflowFolderScanner.Scan(pluginWorkflows);

        Assert.Equal(4, regs.Count);
        Assert.Contains(regs, r => r.RequestType == "PivEnroll" && r.WorkflowDefinitionId == "piv-enroll");
        Assert.Contains(regs, r => r.RequestType == "DcsaVetting" && r.WorkflowDefinitionId == "dcsa-vetting");
        Assert.Contains(regs, r => r.RequestType == "CmsPersonalization" && r.WorkflowDefinitionId == "cms-personalization");
        Assert.Contains(regs, r => r.RequestType == "PacsProvisioning" && r.WorkflowDefinitionId == "pacs-provisioning");
    }
}
