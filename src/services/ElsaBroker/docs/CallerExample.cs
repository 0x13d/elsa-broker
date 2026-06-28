// ── Example: configuring HttpClient with a client cert (Server B side) ────────
// This is NOT part of ElsaBroker itself — it shows how any calling server should
// configure its HttpClient to connect using its mTLS certificate.

using System.Security.Cryptography.X509Certificates;
using System.Net.Http;
using System.Net.Security;

// Load the client PFX issued by certtools
var clientCert = X509CertificateLoader.LoadPkcs12FromFile(
    "certs/ClientA.pfx", password: null);

// Load CA cert to validate the server's certificate
var caCert = X509CertificateLoader.LoadCertificateFromFile("certs/ca.crt");

var handler = new HttpClientHandler();
handler.ClientCertificates.Add(clientCert);

// Validate server cert against our internal CA (not the OS trust store)
handler.ServerCertificateCustomValidationCallback = (message, serverCert, chain, errors) =>
{
    if (serverCert is null) return false;
    var customChain = new X509Chain();
    customChain.ChainPolicy.TrustMode         = X509ChainTrustMode.CustomRootTrust;
    customChain.ChainPolicy.RevocationMode    = X509RevocationMode.NoCheck;
    customChain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;
    customChain.ChainPolicy.CustomTrustStore.Add(caCert);
    return customChain.Build(new X509Certificate2(serverCert));
};

var client = new HttpClient(handler) { BaseAddress = new Uri("https://localhost:5001") };

// Submit a request — no ClientId in body, it's asserted by the certificate
var response = await client.PostAsJsonAsync("/requests", new
{
    requestType = "InvoiceProcess",
    keys = new { InvoiceNumber = "INV-001", VendorCode = "ACME" }
});
