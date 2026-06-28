#!/usr/bin/env python3
"""End-to-end mTLS smoke test for pivlib plugin request types.

Submits each of the four PIV request types (PivEnroll, DcsaVetting,
CmsPersonalization, PacsProvisioning) as the IdmsEmulator client over mutual
TLS, then polls each until it reaches a terminal state.

Prerequisites:
  - Queue API running on https://localhost:5001
  - Processor running and connected
  - Elsa server running with pivlib workflows loaded
  - IdmsEmulator cert generated and added to ClientAllowlist.json
  - requestTypes.pivlib.json entries merged into requestTypes.json

Usage:
  python3 smoke-pivlib.py [certs-dir]
  # Default certs dir: src/services/ElsaBroker/ElsaBroker.Queue/certs
"""
import json, ssl, sys, time, urllib.request

CERTS = sys.argv[1] if len(sys.argv) > 1 else "src/services/ElsaBroker/ElsaBroker.Queue/certs"
BASE  = "https://localhost:5001"

# mTLS context: verify server against internal CA, present IdmsEmulator client cert.
# Pin TLS 1.2 for Kestrel's in-handshake client cert request (see smoke-client.py).
ctx = ssl.create_default_context(cafile=f"{CERTS}/ca.pem")
ctx.load_cert_chain(certfile=f"{CERTS}/IdmsEmulator.pem", keyfile=f"{CERTS}/IdmsEmulator.key")
ctx.maximum_version = ssl.TLSVersion.TLSv1_2

# PIV request types with example payloads
REQUESTS = [
    {
        "requestType": "PivEnroll",
        "keys": {"EmployeeId": "EMP-2026-0042", "AgencyCode": "DOD"},
    },
    {
        "requestType": "DcsaVetting",
        "keys": {"EmployeeId": "EMP-2026-0042", "InvestigationType": "T3"},
    },
    {
        "requestType": "CmsPersonalization",
        "keys": {
            "EmployeeId": "EMP-2026-0042",
            "FascN": "9999999999999999999999999",
            "CardUuid": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
        },
    },
    {
        "requestType": "PacsProvisioning",
        "keys": {
            "FascN": "9999999999999999999999999",
            "FacilityCode": "29001",
        },
    },
]


def call(method, path, body=None):
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(
        f"{BASE}{path}", data=data, method=method,
        headers={"Content-Type": "application/json"},
    )
    with urllib.request.urlopen(req, context=ctx, timeout=10) as r:
        return r.status, json.loads(r.read() or "null")


def submit_and_poll(payload):
    rtype = payload["requestType"]
    print(f"\n--- {rtype} ---")

    # Submit
    status, body = call("POST", "/requests", payload)
    print(f"POST /requests -> {status} {body}")
    cid = body["correlationId"]

    # Poll until terminal
    for _ in range(30):
        status, rec = call("GET", f"/requests/{cid}")
        print(f"GET /requests/{cid} -> {rec['status']}")
        if rec["status"] in ("Completed", "Faulted"):
            print(json.dumps(rec, indent=2))
            return rec["status"] == "Completed"
        time.sleep(1)

    print(f"TIMED OUT waiting for {rtype} to reach terminal status")
    return False


# Run all four PIV request types sequentially
failures = []
for req in REQUESTS:
    if not submit_and_poll(req):
        failures.append(req["requestType"])

if failures:
    print(f"\nFAILED: {', '.join(failures)}")
    sys.exit(1)
else:
    print("\nAll PIV request types completed successfully.")
    sys.exit(0)
