using ElsaBroker.Abstractions;
using ElsaBroker.Switchboard.Components;
using ElsaBroker.Switchboard.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.FluentUI.AspNetCore.Components;

var builder = WebApplication.CreateBuilder(args);

// SQLite database path from configuration
var dbPath = builder.Configuration.GetValue<string>("Switchboard:DatabasePath") ?? "./switchboard.db";

// EF Core SQLite
builder.Services.AddDbContext<SwitchboardDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"));

// Log shipper sink
builder.Services.AddSingleton<ILogShipper, SwitchboardLogShipper>();

// HttpClient must be registered before Fluent UI (Blazor Server requirement)
builder.Services.AddHttpClient();

// Fluent UI
builder.Services.AddFluentUIComponents();

// Blazor Server
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

// Ensure the SQLite database is created on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SwitchboardDbContext>();
    db.Database.EnsureCreated();

    if (app.Environment.IsDevelopment())
        await SwitchboardSeeder.SeedAsync(db);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.MapStaticAssets();
app.UseAntiforgery();

// HTTP ingestion endpoint — Queue/Processor ship events here via HttpJsonLogShipper
app.MapPost("/api/events", async (BrokerLogEvent logEvent, ILogShipper shipper) =>
{
    await shipper.ShipAsync(logEvent);
    return Results.Accepted();
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
