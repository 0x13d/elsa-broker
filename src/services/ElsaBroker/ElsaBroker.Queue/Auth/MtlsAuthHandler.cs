using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace ElsaBroker.Queue.Auth;

/// <summary>
/// Custom ASP.NET Core authentication handler for mTLS.
/// Reads the client certificate from the TLS connection, verifies it was signed
/// by the trusted CA, then looks up the ClientId from ClientAllowlist.json.
/// </summary>
public class MtlsAuthHandler(
    IOptionsMonitor<MtlsAuthOptions>  options,
    ILoggerFactory                    logger,
    UrlEncoder                        encoder,
    IOptions<ClientAllowlistOptions>  allowlist)
    : AuthenticationHandler<MtlsAuthOptions>(options, logger, encoder)
{
    private readonly ILogger _log = logger.CreateLogger<MtlsAuthHandler>();

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var cert = Context.Connection.ClientCertificate;

        if (cert is null)
        {
            _log.LogWarning("mTLS: no client certificate presented from {Remote}",
                Context.Connection.RemoteIpAddress);
            return Task.FromResult(AuthenticateResult.Fail("No client certificate"));
        }

        // 1. Verify chain against our CA
        if (!IsTrustedByOurCa(cert))
        {
            _log.LogWarning("mTLS: certificate '{Subject}' not trusted by internal CA", cert.Subject);
            return Task.FromResult(AuthenticateResult.Fail("Certificate not trusted"));
        }

        // 2. Check not expired
        if (cert.NotAfter < DateTime.UtcNow || cert.NotBefore > DateTime.UtcNow)
        {
            _log.LogWarning("mTLS: certificate '{Subject}' is outside its validity window", cert.Subject);
            return Task.FromResult(AuthenticateResult.Fail("Certificate expired or not yet valid"));
        }

        // 3. Look up thumbprint in allowlist → resolve ClientId
        var thumbprint = cert.Thumbprint.ToUpperInvariant();
        var entry = allowlist.Value.Clients
            .FirstOrDefault(c => string.Equals(c.Thumbprint, thumbprint, StringComparison.OrdinalIgnoreCase));

        if (entry is null)
        {
            _log.LogWarning("mTLS: certificate thumbprint {Thumbprint} not in allowlist", thumbprint);
            return Task.FromResult(AuthenticateResult.Fail("Certificate not in allowlist"));
        }

        _log.LogInformation("mTLS: authenticated as ClientId '{ClientId}' (thumbprint {Thumbprint})",
            entry.ClientId, thumbprint[..8] + "…");

        // 4. Build claims principal — ClientId flows through as a claim
        var claims = new[]
        {
            new Claim(ClaimTypes.Name,           entry.ClientId),
            new Claim(MtlsClaimTypes.ClientId,   entry.ClientId),
            new Claim(MtlsClaimTypes.Thumbprint, thumbprint),
        };

        var identity  = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket    = new AuthenticationTicket(principal, Scheme.Name);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    private bool IsTrustedByOurCa(X509Certificate2 cert)
    {
        var caThumbprint = Options.CaThumbprint;

        // If no CA thumbprint configured, fall back to standard chain validation.
        // Production should always set CaThumbprint.
        if (string.IsNullOrWhiteSpace(caThumbprint))
        {
            _log.LogWarning("mTLS: CaThumbprint not configured — falling back to OS trust store validation");
            return cert.Verify();
        }

        var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode        = X509RevocationMode.NoCheck; // adjust for production CRL/OCSP
        chain.ChainPolicy.VerificationFlags     = X509VerificationFlags.AllowUnknownCertificateAuthority;
        chain.ChainPolicy.TrustMode             = X509ChainTrustMode.CustomRootTrust;

        // Load CA cert by thumbprint from the local machine store, then from certs/ folder
        var caCert = FindCaCertificate(caThumbprint);
        if (caCert is not null)
            chain.ChainPolicy.CustomTrustStore.Add(caCert);

        var valid = chain.Build(cert);
        if (!valid)
        {
            foreach (var status in chain.ChainStatus)
                _log.LogWarning("mTLS: chain error: {Status}", status.StatusInformation);
        }

        return valid;
    }

    private static X509Certificate2? FindCaCertificate(string thumbprint)
    {
        // Try LocalMachine store first (production deployment). Not every
        // platform supports this store — macOS has no LocalMachine 'CA' store
        // and throws — so fall back to certs/ca.crt on disk (dev/Docker).
        try
        {
            using var store = new X509Store(StoreName.CertificateAuthority, StoreLocation.LocalMachine);
            store.Open(OpenFlags.ReadOnly);
            var results = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, validOnly: false);
            if (results.Count > 0) return results[0];
        }
        catch (Exception ex) when (ex is PlatformNotSupportedException or CryptographicException)
        {
            // Store unavailable on this platform — use the on-disk CA below.
        }

        var devPath = Path.Combine(AppContext.BaseDirectory, "certs", "ca.crt");
        return File.Exists(devPath) ? X509CertificateLoader.LoadCertificateFromFile(devPath) : null;
    }
}

public class MtlsAuthOptions : AuthenticationSchemeOptions
{
    /// <summary>Thumbprint of the CA cert that signed all client certs.</summary>
    public string CaThumbprint { get; set; } = string.Empty;
}

public static class MtlsClaimTypes
{
    public const string ClientId   = "elsabroker:clientid";
    public const string Thumbprint = "elsabroker:thumbprint";
}
