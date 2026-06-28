using Microsoft.Extensions.Logging.Abstractions;
using ElsaBroker.Data.Entities;
using ElsaBroker.Data.Registry;
using Xunit;

namespace ElsaBroker.Tests;

public class RequestTypeRegistryTests
{
    private static RequestTypeRegistry NewRegistry() => new(NullLogger<RequestTypeRegistry>.Instance);

    private static RequestTypeModel Json(string client, string type, params string[] keys) =>
        new(client, type, keys, AllowAdditionalKeys: false, Source: "json");

    [Fact]
    public void Get_returns_json_loaded_model()
    {
        var registry = NewRegistry();
        registry.Load([Json("ClientA", "InvoiceProcess", "InvoiceNumber")], []);

        var model = registry.Get("ClientA", "InvoiceProcess");

        Assert.NotNull(model);
        Assert.Equal("json", model!.Source);
        Assert.Contains("InvoiceNumber", model.RequiredKeys);
    }

    [Theory]
    [InlineData("clienta", "invoiceprocess")]
    [InlineData("CLIENTA", "INVOICEPROCESS")]
    [InlineData("ClientA", "invoiceProcess")]
    public void Get_is_case_insensitive_on_client_and_type(string client, string type)
    {
        var registry = NewRegistry();
        registry.Load([Json("ClientA", "InvoiceProcess")], []);

        Assert.NotNull(registry.Get(client, type));
    }

    [Fact]
    public void Get_returns_null_for_unknown_pair()
    {
        var registry = NewRegistry();
        registry.Load([Json("ClientA", "InvoiceProcess")], []);

        Assert.Null(registry.Get("ClientA", "PolicyRenewal"));
        Assert.Null(registry.Get("ClientZ", "InvoiceProcess"));
    }

    [Fact]
    public void Database_definition_overrides_json_for_same_key()
    {
        var registry = NewRegistry();
        var dbDef = new RequestTypeDefinition
        {
            ClientId            = "ClientA",
            RequestType         = "InvoiceProcess",
            RequiredKeys        = "PoNumber",
            AllowAdditionalKeys = true,
            IsActive            = true,
        };

        registry.Load([Json("ClientA", "InvoiceProcess", "InvoiceNumber")], [dbDef]);

        var model = registry.Get("ClientA", "InvoiceProcess");
        Assert.NotNull(model);
        Assert.Equal("database", model!.Source);
        Assert.Contains("PoNumber", model.RequiredKeys);
        Assert.DoesNotContain("InvoiceNumber", model.RequiredKeys);
        Assert.True(model.AllowAdditionalKeys);
    }

    [Fact]
    public void Inactive_database_definitions_are_ignored()
    {
        var registry = NewRegistry();
        var inactive = new RequestTypeDefinition
        {
            ClientId        = "ClientA",
            RequestType     = "InvoiceProcess",
            RequiredKeys    = "PoNumber",
            IsActive        = false,
        };

        registry.Load([Json("ClientA", "InvoiceProcess", "InvoiceNumber")], [inactive]);

        var model = registry.Get("ClientA", "InvoiceProcess");
        Assert.Equal("json", model!.Source);
        Assert.Contains("InvoiceNumber", model.RequiredKeys);
    }
}
