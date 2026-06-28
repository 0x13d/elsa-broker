using MassTransit;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ElsaBroker.Contracts;
using ElsaBroker.Data;
using ElsaBroker.Data.Entities;
using ElsaBroker.Processor.Consumers;
using ElsaBroker.Abstractions;
using ElsaBroker.Data.Logging;
using Xunit;

namespace ElsaBroker.Tests;

/// <summary>
/// Behaviour tests for <see cref="SubmitRequestConsumer"/> using the MassTransit
/// in-memory test harness and the EF Core in-memory provider. These exercise the
/// status-transition contract (Queued → Processing → Completed/Faulted) and the
/// redelivery path that the whole broker depends on.
/// </summary>
public class SubmitRequestConsumerTests
{
    /// <summary>A handler whose behaviour each test controls.</summary>
    private sealed class StubHandler(string requestType, Func<ISubmitRequest, RequestResult> behaviour)
        : IRequestHandler
    {
        public string RequestType => requestType;
        public Task<RequestResult> HandleAsync(ISubmitRequest request, CancellationToken ct)
            => Task.FromResult(behaviour(request));
    }

    private static ServiceProvider BuildProvider(Action<IServiceCollection> registerHandlers)
    {
        var dbName   = "consumer-tests-" + Guid.NewGuid();
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddDbContext<BrokerDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddNullLogShipper();
        registerHandlers(services);
        services.AddMassTransitTestHarness(x => x.AddConsumer<SubmitRequestConsumer>());
        return services.BuildServiceProvider(true);
    }

    private static async Task SeedRecordAsync(IServiceProvider provider, Guid id, string requestType)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BrokerDbContext>();
        db.RequestRecords.Add(new RequestRecord
        {
            CorrelationId = id,
            ClientId      = "ClientA",
            RequestType   = requestType,
            Status        = RequestStatus.Queued,
            SubmittedAt   = DateTime.UtcNow,
            UpdatedAt     = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private static async Task<RequestRecord?> ReadRecordAsync(IServiceProvider provider, Guid id)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BrokerDbContext>();
        return await db.RequestRecords.AsNoTracking().FirstOrDefaultAsync(r => r.CorrelationId == id);
    }

    private static object NewMessage(Guid id, string requestType = "InvoiceProcess") => new
    {
        CorrelationId = id,
        ClientId      = "ClientA",
        RequestType   = requestType,
        Keys          = new Dictionary<string, string> { ["InvoiceNumber"] = "INV-1" },
        Payload       = new Dictionary<string, object>(),
        SubmittedAt   = DateTime.UtcNow,
    };

    [Fact]
    public async Task Successful_handler_marks_record_completed_with_output()
    {
        await using var provider = BuildProvider(s =>
            s.AddScoped<IRequestHandler>(_ =>
                new StubHandler("InvoiceProcess", _ => new RequestResult(true, "done", null))));
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        try
        {
            var id = Guid.NewGuid();
            await SeedRecordAsync(provider, id, "InvoiceProcess");

            await harness.Bus.Publish<ISubmitRequest>(NewMessage(id));

            Assert.True(await harness.Consumed.Any<ISubmitRequest>());
            var record = await ReadRecordAsync(provider, id);
            Assert.Equal(RequestStatus.Completed, record!.Status);
            Assert.Equal("done", record.Result);
            Assert.Null(record.ErrorDetail);
        }
        finally { await harness.Stop(); }
    }

    [Fact]
    public async Task Unknown_request_type_faults_the_record()
    {
        // Handler registered for a different type than the message carries.
        await using var provider = BuildProvider(s =>
            s.AddScoped<IRequestHandler>(_ =>
                new StubHandler("SomethingElse", _ => new RequestResult(true, "x", null))));
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        try
        {
            var id = Guid.NewGuid();
            await SeedRecordAsync(provider, id, "InvoiceProcess");

            await harness.Bus.Publish<ISubmitRequest>(NewMessage(id));

            Assert.True(await harness.Consumed.Any<ISubmitRequest>());
            var record = await ReadRecordAsync(provider, id);
            Assert.Equal(RequestStatus.Faulted, record!.Status);
            Assert.Contains("No handler", record.ErrorDetail);
        }
        finally { await harness.Stop(); }
    }

    [Fact]
    public async Task Throwing_handler_faults_the_record_with_message()
    {
        await using var provider = BuildProvider(s =>
            s.AddScoped<IRequestHandler>(_ =>
                new StubHandler("InvoiceProcess", _ => throw new InvalidOperationException("boom"))));
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        try
        {
            var id = Guid.NewGuid();
            await SeedRecordAsync(provider, id, "InvoiceProcess");

            await harness.Bus.Publish<ISubmitRequest>(NewMessage(id));

            Assert.True(await harness.Consumed.Any<ISubmitRequest>());
            var record = await ReadRecordAsync(provider, id);
            Assert.Equal(RequestStatus.Faulted, record!.Status);
            Assert.Equal("boom", record.ErrorDetail);
        }
        finally { await harness.Stop(); }
    }

    [Fact]
    public async Task Missing_record_is_skipped_without_throwing()
    {
        await using var provider = BuildProvider(s =>
            s.AddScoped<IRequestHandler>(_ =>
                new StubHandler("InvoiceProcess", _ => new RequestResult(true, "done", null))));
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        try
        {
            var id = Guid.NewGuid(); // no record seeded

            await harness.Bus.Publish<ISubmitRequest>(NewMessage(id));

            // Consumer should consume and return cleanly (no fault, no record).
            Assert.True(await harness.Consumed.Any<ISubmitRequest>());
            Assert.Null(await ReadRecordAsync(provider, id));
        }
        finally { await harness.Stop(); }
    }

    [Fact]
    public async Task Redelivery_of_same_correlation_id_stays_completed()
    {
        var calls = 0;
        await using var provider = BuildProvider(s =>
            s.AddScoped<IRequestHandler>(_ =>
                new StubHandler("InvoiceProcess", _ =>
                {
                    Interlocked.Increment(ref calls);
                    return new RequestResult(true, "done", null);
                })));
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        try
        {
            var id = Guid.NewGuid();
            await SeedRecordAsync(provider, id, "InvoiceProcess");

            await harness.Bus.Publish<ISubmitRequest>(NewMessage(id));
            await harness.Bus.Publish<ISubmitRequest>(NewMessage(id));

            // Wait until both deliveries have run the handler.
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (Volatile.Read(ref calls) < 2 && DateTime.UtcNow < deadline)
                await Task.Delay(50);

            var record = await ReadRecordAsync(provider, id);
            Assert.Equal(RequestStatus.Completed, record!.Status);
            Assert.True(calls >= 2, "handler should run for each delivery (idempotency is at the record level)");
        }
        finally { await harness.Stop(); }
    }
}
