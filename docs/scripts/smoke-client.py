#!/usr/bin/env python3
"""End-to-end mTLS smoke test against a running Queue API.

Submits an InvoiceProcess request as ClientA over mutual TLS, then polls the
status endpoint until the request reaches a terminal state. Exits non-zero if
it does not complete.
"""
import json, ssl, sys, time, urllib.request

CERTS = sys.argv[1] if len(sys.argv) > 1 else "src/services/ElsaBroker/ElsaBroker.Queue/certs"
BASE  = "https://localhost:5001"

# Full mutual TLS: verify the server against our internal CA (the server cert's
# SAN includes "localhost"), and present ClientA's cert + key for client auth.
# The cert tool also emits DER .crt files (read directly by the .NET services);
# Python's TLS stack needs the PEM forms.
ctx = ssl.create_default_context(cafile=f"{CERTS}/ca.pem")
ctx.load_cert_chain(certfile=f"{CERTS}/ClientA.pem", keyfile=f"{CERTS}/ClientA.key")
# Kestrel's RequireCertificate uses TLS 1.3 post-handshake client auth, which
# OpenSSL resets; pin TLS 1.2 so the client cert is requested in-handshake.
ctx.maximum_version = ssl.TLSVersion.TLSv1_2

def call(method, path, body=None):
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(f"{BASE}{path}", data=data, method=method,
                                 headers={"Content-Type": "application/json"})
    with urllib.request.urlopen(req, context=ctx, timeout=10) as r:
        return r.status, json.loads(r.read() or "null")

# 1. Submit
status, body = call("POST", "/requests", {
    "requestType": "InvoiceProcess",
    "keys": {"InvoiceNumber": "INV-001", "VendorCode": "ACME"},
})
print(f"POST /requests -> {status} {body}")
cid = body["correlationId"]

# 2. Poll until terminal
for _ in range(20):
    status, rec = call("GET", f"/requests/{cid}")
    print(f"GET /requests/{cid} -> {rec['status']}")
    if rec["status"] in ("Completed", "Faulted"):
        print(json.dumps(rec, indent=2))
        sys.exit(0 if rec["status"] == "Completed" else 2)
    time.sleep(1)

print("TIMED OUT waiting for terminal status")
sys.exit(1)
