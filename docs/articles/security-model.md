# Security model

ElsaBroker's central security property: **authorization is asserted by the client certificate, not by the
caller.** A request cannot claim to be from another client, and a client cannot act on request types it
was not registered for.

## mTLS perimeter

```text
Client (Server B)
  TLS handshake: presents ClientA.pfx (signed by the internal CA)
        │
        ▼
  Kestrel: RequireCertificate — connections without a cert are rejected
        │
        ▼
  MtlsAuthHandler
    1. Cert chains to the internal CA?      (chain validation)
    2. Cert within its validity window?     (expiry check)
    3. Thumbprint in ClientAllowlist.json?  (allowlist check)
    → stamps the ClientId claim onto the request
        │
        ▼
  RequestEndpoints
    - ClientId comes from the cert claim, never the body
    - Registry authorizes (ClientId, RequestType)
    - A client may only poll its own RequestRecords
```

## Properties

- **No spoofing.** `ClientId` is derived from the validated certificate. Any code path that reads
  `ClientId` from the request payload is a vulnerability.
- **Rotation without secret coordination.** Issue a new cert and add its thumbprint to the allowlist;
  remove the old one when ready. No shared secrets to rotate.
- **Immediate revocation.** Delete a thumbprint from `ClientAllowlist.json` to revoke a client.
- **Private keys stay put.** Each party holds its own key; only public certs + the CA cert are shared.

## Certificate lifecycle

`ElsaBroker.CertTools` issues the CA, the server cert, and client certs. Operationally:

1. `dotnet run -- client <id>` to mint a client cert.
2. Deliver `<id>.pfx` to the client securely; send them `ca.crt` so they can validate the server.
3. Add the thumbprint to `ClientAllowlist.json`.
4. Register the client's `(ClientId, RequestType)` entries (JSON or DB).

## Supply chain & privacy

- **No telemetry.** The broker makes no outbound calls beyond SQL Server and the bus.
- **Secrets never committed.** `certs/`, `*.pfx`, `*.key`, and `*.pem` are git-ignored.
- **Dependency hygiene.** Pin package versions; review `dotnet list package --vulnerable` and an SBOM as
  part of a trust report.
- **Docs are offline too.** The architecture diagram renders to a committed SVG with no third-party
  diagram service involved.

These guarantees are the domain of the **security** agent on the team; trust-relevant decisions are
recorded as ADRs.
