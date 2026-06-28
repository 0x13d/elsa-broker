using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using MassTransit;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.EntityFrameworkCore;
using ElsaBroker.Abstractions;
using ElsaBroker.Contracts;
using ElsaBroker.Data;
using ElsaBroker.Data.Logging;
using ElsaBroker.Data.Registry;
using ElsaBroker.Queue.Auth;
using ElsaBroker.Queue.Endpoints;

var builder = WebApplication.CreateBuilder(args);
var cfg     = builder.Configuration;

// ── Kestrel: two listeners ────────────────────────────────────────────────────
//   5001  public mTLS ingress  — RequireCertificate; trust decided by MtlsAuthHandler.
//   5080  internal callbacks   — plain HTTP for the Elsa workflow to finalize a
//         request (shared-secret auth). mTLS endpoints stay safe on 5080 because
//         MtlsAuthHandler fails when no client cert is present. Publish 5080 only
//         on the internal/Docker network.
builder.WebHost.ConfigureKestrel(kestrel =>
{
    kestrel.ListenAnyIP(5001, listen => listen.UseHttps(https =>
    {
        https.ClientCertificateMode = ClientCertificateMode.RequireCertificate;

        // Accept any presented client cert at the TLS layer; the real trust
        // decision (chain-to-internal-CA + validity + allowlist) is made by
        // MtlsAuthHandler. Without this, Kestrel's default validation rejects
        // certs signed by our internal CA (not in the OS trust store) and
        // resets the connection before the app's auth handler ever runs.
        https.ClientCertificateValidation = (_, _, _) => true;

        var serverCertPath = cfg["Mtls:ServerCertPath"]
            ?? Path.Combine(AppContext.BaseDirectory, "certs", "localhost.pfx");

        if (File.Exists(serverCertPath))
            https.ServerCertificate = X509CertificateLoader.LoadPkcs12FromFile(serverCertPath, password: null);
        else
            Console.WriteLine($"[WARN] Server cert not found at {serverCertPath}. Run: dotnet run --project ElsaBroker.CertTools -- server localhost");
    }));

    kestrel.ListenAnyIP(5080); // internal Elsa callbacks (HTTP, shared-secret)
});

// ── EF Core ───────────────────────────────────────────────────────────────────
builder.Services.AddDbContext<BrokerDbContext>(opt =>
    opt.UseSqlServer(cfg.GetConnectionString("BrokerDb")));

// ── Registry ──────────────────────────────────────────────────────────────────
builder.Services.AddSingleton<RequestTypeRegistry>();
builder.Services.AddScoped<RegistryLoader>();

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

// ── Client allowlist (ClientAllowlist.json) ───────────────────────────────────
builder.Services.AddOptions<ClientAllowlistOptions>()
    .Configure(opts =>
    {
        var allowlistPath = Path.Combine(AppContext.BaseDirectory, "ClientAllowlist.json");
        if (!File.Exists(allowlistPath))
        {
            Console.WriteLine("[WARN] ClientAllowlist.json not found — no clients will be authorised.");
            return;
        }
        var file = System.Text.Json.JsonSerializer.Deserialize<ClientAllowlistFile>(
            File.ReadAllText(allowlistPath),
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (file?.Clients is not null)
            opts.Clients.AddRange(file.Clients.Select(c => new ClientEntry
            {
                ClientId    = c.ClientId,
                Thumbprint  = c.Thumbprint.ToUpperInvariant().Replace(":", "").Replace(" ", ""),
                Description = c.Description ?? string.Empty,
            }));
    });

// ── mTLS authentication ───────────────────────────────────────────────────────
builder.Services
    .AddAuthentication("mtls")
    .AddScheme<MtlsAuthOptions, MtlsAuthHandler>("mtls", opts =>
    {
        opts.CaThumbprint = cfg["Mtls:CaThumbprint"] ?? string.Empty;
    });

builder.Services.AddAuthorization();

// ── MassTransit (SQL Server transport) ───────────────────────────────────────
builder.Services.AddOptions<SqlTransportOptions>().Configure(o =>
{
    o.ConnectionString = cfg.GetConnectionString("BrokerDb");
});
// Provisions the SQL transport schema (queues/topics tables) on startup.
builder.Services.AddSqlServerMigrationHostedService();

builder.Services.AddMassTransit(mt =>
{
    mt.AddEntityFrameworkOutbox<BrokerDbContext>(o =>
    {
        o.UseSqlServer();
        o.UseBusOutbox();
    });

    mt.UsingSqlServer((ctx, sql) =>
    {
        sql.ConfigureEndpoints(ctx);
    });
});

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

// ── Migrations + registry on startup ─────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<BrokerDbContext>();
    await db.Database.MigrateAsync();

    var loader   = scope.ServiceProvider.GetRequiredService<RegistryLoader>();
    var jsonPath = Path.Combine(AppContext.BaseDirectory, "requestTypes.json");
    await loader.LoadAsync(jsonPath);
}

app.MapRequestEndpoints();

// ── Internal callback: the Elsa workflow finalizes a request ──────────────────
// Reached on the internal listener (5080) with a shared secret. Not mTLS — the
// Elsa demo container can't present a client cert — so it is gated by the secret
// and should only be exposed on the internal network.
app.MapPost("/internal/requests/{id:guid}/result", async (
    Guid id, CallbackResult body, HttpContext http, BrokerDbContext db,
    ILogShipper shipper, CancellationToken ct) =>
{
    var expected = cfg["Elsa:CallbackSecret"] ?? string.Empty;
    var provided = http.Request.Headers["X-Callback-Secret"].ToString();
    if (string.IsNullOrEmpty(expected) ||
        !CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(provided),
            System.Text.Encoding.UTF8.GetBytes(expected)))
        return Results.Unauthorized();

    var record = await db.RequestRecords.FindAsync([id], ct);
    if (record is null) return Results.NotFound();

    record.Status = string.Equals(body.Status, RequestStatus.Completed, StringComparison.OrdinalIgnoreCase)
        ? RequestStatus.Completed
        : RequestStatus.Faulted;
    record.Result      = body.Result?.GetRawText();   // result is an arbitrary JSON value
    record.ErrorDetail = body.Error;
    record.UpdatedAt   = DateTime.UtcNow;
    await db.SaveChangesAsync(ct);

    await shipper.ShipAsync(new BrokerLogEvent
    {
        Timestamp     = DateTimeOffset.UtcNow,
        EventType     = "CallbackReceived",
        Level         = "Information",
        Source        = "Queue",
        Message       = $"Callback for {id}: {record.Status}",
        CorrelationId = id.ToString(),
        RequestType   = record.RequestType,
        ClientId      = record.ClientId,
        Properties    = new Dictionary<string, object?> { ["status"] = record.Status },
    }, ct);

    return Results.NoContent();
}).AllowAnonymous();

app.Run();

// ── Local types ───────────────────────────────────────────────────────────────
record ClientAllowlistFile(List<ClientAllowlistEntry>? Clients);
record ClientAllowlistEntry(string ClientId, string Thumbprint, string? Description);
record CallbackResult(string Status, System.Text.Json.JsonElement? Result, string? Error);
