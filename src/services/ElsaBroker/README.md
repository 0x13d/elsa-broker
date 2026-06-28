# ElsaBroker

Generic message queuing layer — MassTransit SQL transport, SQL Server backend,
mTLS client certificate authentication.

## Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) — 8 GB RAM recommended

---

## Quick start

### 1. Start SQL Server

```bash
docker compose up -d
docker compose ps   # wait for 'healthy' (~30 seconds first run)
```

### 2. Generate certificates (one-time setup)

```bash
cd ElsaBroker.CertTools

# Create internal CA
dotnet run -- ca

# Create server cert for the Queue service
dotnet run -- server localhost

# Create one client cert per authorized server
dotnet run -- client ClientA
dotnet run -- client ClientB
```

All certs land in `certs/`. The tool prints each thumbprint — copy them.

### 3. Register client thumbprints

Edit `ElsaBroker.Queue/ClientAllowlist.json` and fill in the thumbprints
printed by the cert tool:

```json
{
  "clients": [
    { "clientId": "ClientA", "thumbprint": "<paste from certtools output>" },
    { "clientId": "ClientB", "thumbprint": "<paste from certtools output>" }
  ]
}
```

### 4. Set the CA thumbprint in config

In `ElsaBroker.Queue/appsettings.json` (or user secrets), set:

```json
"Mtls": {
  "ServerCertPath": "certs/localhost.pfx",
  "CaThumbprint":   "<thumbprint printed when you ran: dotnet run -- ca>"
}
```

### 5. Run the services

```bash
# Terminal 1
cd ElsaBroker.Queue && dotnet run

# Terminal 2
cd ElsaBroker.Processor && dotnet run
```

EF Core migrations run automatically on startup.

### 6. Test with curl (using a client cert)

```bash
curl --cert certs/ClientA.pfx \
     --cacert certs/ca.crt \
     -X POST https://localhost:5001/requests \
     -H "Content-Type: application/json" \
     -d '{"requestType":"InvoiceProcess","keys":{"InvoiceNumber":"INV-001","VendorCode":"ACME"}}'
```

### 7. Poll for status

```bash
curl --cert certs/ClientA.pfx --cacert certs/ca.crt \
     https://localhost:5001/requests/<correlationId>
```

---

## Security model

```
Client Server (Server B)
  TLS handshake: presents ClientA.pfx (signed by internal CA)
        │
        ▼
  Kestrel: RequireCertificate — rejects connections with no cert
        │
        ▼
  MtlsAuthHandler
    1. Cert signed by internal CA?          (chain validation)
    2. Cert within validity window?         (expiry check)
    3. Thumbprint in ClientAllowlist.json?  (allowlist check)
    → stamps ClientId claim onto request context
        │
        ▼
  RequestEndpoints
    - ClientId taken from cert claim (never from request body)
    - Registry validates RequestType is allowed for this ClientId
    - Clients can only poll their own RequestRecords
```

**mTLS properties:**
- Private keys never leave their respective machines
- Certificate rotation = issue new cert + update allowlist, no secret coordination
- A compromised cert can be immediately revoked by removing it from the allowlist
- ClientId spoofing is impossible — it's asserted by the certificate, not the caller

---

## Adding a new authorized client

1. `cd ElsaBroker.CertTools && dotnet run -- client <newClientId>`
2. Send the generated `<newClientId>.pfx` to the client team securely
3. Send them `certs/ca.crt` so they can validate the server cert
4. Add the thumbprint to `ClientAllowlist.json` (no redeploy needed if using hot-reload; restart if not)
5. Add the new `ClientId` + `RequestType` entries to `requestTypes.json` or the DB

## Adding a new request type

**Repo (default):** edit `ElsaBroker.Queue/requestTypes.json`, add a handler
class in `ElsaBroker.Processor/Handlers/`, register in `Program.cs`.

**Database (client-defined):** insert into `RequestTypeDefinitions` table.

## Project layout

```
ElsaBroker.Contracts     Shared ISubmitRequest interface — never changes
ElsaBroker.Data          EF Core, entities, registry (shared by both services)
ElsaBroker.Queue         ASP.NET Core API — mTLS auth, validation, enqueue, poll
ElsaBroker.Processor     Worker Service — consume, dispatch, update audit
ElsaBroker.CertTools     CLI tool — CA + cert generation for dev and ops
docs/CallerExample.cs   How any calling server configures its HttpClient
```
