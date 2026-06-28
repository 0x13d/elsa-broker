using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

// ── ElsaBroker Certificate Tool ────────────────────────────────────────────────
// Usage:
//   dotnet run -- ca                          Create CA cert (run once)
//   dotnet run -- server <hostname>           Create server cert signed by CA
//   dotnet run -- client <clientId>           Create client cert signed by CA
//   dotnet run -- thumbprint <path.pfx>       Print thumbprint of a cert
//
// All output goes to ./certs/ relative to working directory.
// ─────────────────────────────────────────────────────────────────────────────

if (args.Length == 0) { PrintHelp(); return; }

var certsDir = Path.Combine(Directory.GetCurrentDirectory(), "certs");
Directory.CreateDirectory(certsDir);

switch (args[0].ToLowerInvariant())
{
    case "ca":
        CreateCa(certsDir);
        break;

    case "server" when args.Length >= 2:
        CreateSignedCert(certsDir, args[1], isClient: false);
        break;

    case "client" when args.Length >= 2:
        CreateSignedCert(certsDir, args[1], isClient: true);
        break;

    case "thumbprint" when args.Length >= 2:
        PrintThumbprint(args[1]);
        break;

    default:
        PrintHelp();
        break;
}

// ── CA creation ───────────────────────────────────────────────────────────────
static void CreateCa(string certsDir)
{
    using var key = RSA.Create(4096);
    var req = new CertificateRequest(
        "CN=ElsaBroker-CA, O=ElsaBroker, OU=Internal",
        key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

    req.CertificateExtensions.Add(
        new X509BasicConstraintsExtension(certificateAuthority: true, hasPathLengthConstraint: true, pathLengthConstraint: 1, critical: true));
    req.CertificateExtensions.Add(
        new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, critical: true));
    req.CertificateExtensions.Add(
        new X509SubjectKeyIdentifierExtension(req.PublicKey, critical: false));

    var notBefore = DateTimeOffset.UtcNow.AddDays(-1);
    var notAfter  = notBefore.AddYears(10);

    using var caCert = req.CreateSelfSigned(notBefore, notAfter);

    // Export PFX (includes private key — keep this safe)
    var pfxPath = Path.Combine(certsDir, "ca.pfx");
    File.WriteAllBytes(pfxPath, caCert.Export(X509ContentType.Pfx));

    // Export public cert only (distribute to clients for trust)
    var crtPath = Path.Combine(certsDir, "ca.crt");
    File.WriteAllBytes(crtPath, caCert.Export(X509ContentType.Cert));

    Console.WriteLine($"CA certificate created:");
    Console.WriteLine($"  PFX  (keep private): {pfxPath}");
    Console.WriteLine($"  CRT  (distribute):   {crtPath}");
    Console.WriteLine($"  Thumbprint: {caCert.Thumbprint}");
    Console.WriteLine();
    Console.WriteLine("Next steps:");
    Console.WriteLine("  Server cert : dotnet run -- server <hostname>");
    Console.WriteLine("  Client cert : dotnet run -- client <clientId>");
}

// ── Signed cert (server or client) ───────────────────────────────────────────
static void CreateSignedCert(string certsDir, string name, bool isClient)
{
    var caPath = Path.Combine(certsDir, "ca.pfx");
    if (!File.Exists(caPath))
    {
        Console.Error.WriteLine("ca.pfx not found. Run `dotnet run -- ca` first.");
        Environment.Exit(1);
    }

    // NOTE: EphemeralKeySet is unsupported on macOS (PlatformNotSupportedException);
    // the default key storage works cross-platform for this dev tool.
    using var caCert = X509CertificateLoader.LoadPkcs12FromFile(caPath, password: null);

    using var key = RSA.Create(2048);
    var subject  = isClient
        ? $"CN={name}, O=ElsaBroker-Client, OU=Clients"
        : $"CN={name}, O=ElsaBroker-Server, OU=Services";

    var req = new CertificateRequest(subject, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

    req.CertificateExtensions.Add(
        new X509BasicConstraintsExtension(certificateAuthority: false, hasPathLengthConstraint: false, pathLengthConstraint: 0, critical: true));

    if (isClient)
    {
        // Client auth OID
        req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            [new Oid("1.3.6.1.5.5.7.3.2")], critical: true));
        req.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: true));
    }
    else
    {
        // Server auth OID + SANs
        req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            [new Oid("1.3.6.1.5.5.7.3.1")], critical: true));
        req.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, critical: true));

        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddDnsName(name);
        sanBuilder.AddDnsName("localhost");
        sanBuilder.AddIpAddress(System.Net.IPAddress.Loopback);
        req.CertificateExtensions.Add(sanBuilder.Build());
    }

    req.CertificateExtensions.Add(
        new X509SubjectKeyIdentifierExtension(req.PublicKey, critical: false));

    // Authority Key Identifier — links the cert to its issuing CA. Strict
    // verifiers (OpenSSL 3 / Python) reject a chain without it.
    req.CertificateExtensions.Add(
        X509AuthorityKeyIdentifierExtension.CreateFromCertificate(
            caCert, includeKeyIdentifier: true, includeIssuerAndSerial: false));

    var serial     = new byte[16];
    RandomNumberGenerator.Fill(serial);
    var notBefore  = DateTimeOffset.UtcNow.AddDays(-1);
    var notAfter   = notBefore.AddYears(2);

    using var signedCert = req.Create(caCert, notBefore, notAfter, serial);
    using var certWithKey = signedCert.CopyWithPrivateKey(key);

    var safeName = name.Replace("*", "wildcard").Replace(" ", "_");
    var pfxPath  = Path.Combine(certsDir, $"{safeName}.pfx");
    var crtPath  = Path.Combine(certsDir, $"{safeName}.crt");

    File.WriteAllBytes(pfxPath, certWithKey.Export(X509ContentType.Pfx));
    File.WriteAllBytes(crtPath, signedCert.Export(X509ContentType.Cert));

    var kind = isClient ? "Client" : "Server";
    Console.WriteLine($"{kind} certificate created for '{name}':");
    Console.WriteLine($"  PFX  (private key + cert): {pfxPath}");
    Console.WriteLine($"  CRT  (cert only):          {crtPath}");
    Console.WriteLine($"  Thumbprint: {signedCert.Thumbprint}");
    if (isClient)
    {
        Console.WriteLine();
        Console.WriteLine($"  Register this client in ClientAllowlist.json:");
        Console.WriteLine($"  {{ \"clientId\": \"{name}\", \"thumbprint\": \"{signedCert.Thumbprint}\" }}");
    }
}

static void PrintThumbprint(string pfxPath)
{
    if (!File.Exists(pfxPath)) { Console.Error.WriteLine($"File not found: {pfxPath}"); return; }
    using var cert = X509CertificateLoader.LoadPkcs12FromFile(pfxPath, password: null);
    Console.WriteLine($"Subject:    {cert.Subject}");
    Console.WriteLine($"Thumbprint: {cert.Thumbprint}");
    Console.WriteLine($"NotAfter:   {cert.NotAfter:yyyy-MM-dd}");
}

static void PrintHelp()
{
    Console.WriteLine("ElsaBroker Certificate Tool");
    Console.WriteLine("  dotnet run -- ca                   Create CA (run once)");
    Console.WriteLine("  dotnet run -- server <hostname>    Create server cert");
    Console.WriteLine("  dotnet run -- client <clientId>    Create client cert");
    Console.WriteLine("  dotnet run -- thumbprint <file>    Show cert thumbprint");
}
