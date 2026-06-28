using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ElsaBroker.Abstractions;
using ElsaBroker.Data;
using ElsaBroker.Data.Logging;
using ElsaBroker.Data.Registry;
using ElsaBroker.Processor.Consumers;
using ElsaBroker.Processor.Handlers;

var builder = Host.CreateApplicationBuilder(args);
var cfg     = builder.Configuration;

builder.Services.AddDbContext<BrokerDbContext>(opt =>
    opt.UseSqlServer(cfg.GetConnectionString("BrokerDb")));

builder.Services.AddSingleton<RequestTypeRegistry>();
builder.Services.AddScoped<RegistryLoader>();

// ── Elsa workflow dispatch ───────────────────────────────────────────────────
// Scan the shared workflows folder (the same one the Elsa server loads) and
// register one async dispatch handler per discovered request type. Each request
// type is handled by its Elsa workflow — no hand-written C# handler needed.
var workflowPaths = cfg.GetSection("Workflows:Paths").Get<string[]>();
if (workflowPaths is null || workflowPaths.Length == 0)
{
    var singlePath = cfg["Elsa:WorkflowsPath"];
    if (string.IsNullOrWhiteSpace(singlePath))
        singlePath = Path.Combine(AppContext.BaseDirectory, "workflows");
    workflowPaths = [singlePath];
}
var workflowRegistrations = WorkflowFolderScanner.Scan(workflowPaths);
builder.Services.AddSingleton(new WorkflowRegistry(workflowRegistrations));

var elsaOptions = new ElsaDispatchOptions { WorkflowsPath = workflowPaths[0] };
cfg.GetSection("Elsa").Bind(elsaOptions);
builder.Services.AddSingleton(elsaOptions);
builder.Services.AddHttpClient();

foreach (var reg in workflowRegistrations)
{
    var r = reg;
    builder.Services.AddSingleton<IRequestHandler>(sp => new ElsaDispatchHandler(
        r.RequestType, r.WorkflowDefinitionId,
        sp.GetRequiredService<IHttpClientFactory>().CreateClient("elsa"),
        sp.GetRequiredService<ElsaDispatchOptions>(),
        sp.GetRequiredService<ILogger<ElsaDispatchHandler>>()));
}
Console.WriteLine($"[elsa] Registered {workflowRegistrations.Count} workflow request type(s) from [{string.Join(", ", workflowPaths)}].");

// ── Log shipper ────────────────────────────────────────────────────────────
var logShipperSink = cfg["LogShipper:Sink"];  // "Console", "Http", or null/empty → Null
if (string.Equals(logShipperSink, "Console", StringComparison.OrdinalIgnoreCase))
    builder.Services.AddConsoleLogShipper();
else if (string.Equals(logShipperSink, "Http", StringComparison.OrdinalIgnoreCase))
    builder.Services.AddHttpJsonLogShipper(o =>
    {
        o.SinkUrl = cfg["LogShipper:SinkUrl"] ?? o.SinkUrl;
        o.ApiKey  = cfg["LogShipper:ApiKey"];
    });
else
    builder.Services.AddNullLogShipper();

builder.Services.AddOptions<SqlTransportOptions>().Configure(o =>
{
    o.ConnectionString = cfg.GetConnectionString("BrokerDb");
});
// Provisions the SQL transport schema (queues/topics tables) on startup.
builder.Services.AddSqlServerMigrationHostedService();

builder.Services.AddMassTransit(mt =>
{
    mt.AddConsumer<SubmitRequestConsumer>();

    mt.AddEntityFrameworkOutbox<BrokerDbContext>(o =>
    {
        o.UseSqlServer();
        o.UseBusOutbox();
    });

    mt.UsingSqlServer((ctx, sql) =>
    {
        sql.ReceiveEndpoint("submit-request", e =>
        {
            e.ConfigureConsumer<SubmitRequestConsumer>(ctx);
        });
    });
});

var host = builder.Build();

using (var scope = host.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<BrokerDbContext>();
    await db.Database.MigrateAsync();

    var loader   = scope.ServiceProvider.GetRequiredService<RegistryLoader>();
    var jsonPath = Path.Combine(AppContext.BaseDirectory, "requestTypes.json");
    await loader.LoadAsync(jsonPath);
}

await host.RunAsync();
