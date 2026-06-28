# Test Results

These reports are generated from the `ElsaBroker.Tests` suite at build time and embedded into the site.
They are produced offline by [`build-reports.sh`](https://github.com/ariugwu) — xUnit's HTML logger for
the test run, and [ReportGenerator](https://github.com/danielpalme/ReportGenerator) over Coverlet's
Cobertura output for coverage.

<div class="test-results-links">

- 📋 **[Unit test report](tests/index.html)** — every test, grouped by class, with pass/fail and timing.
- 📊 **[Code coverage report](coverage/index.html)** — line/branch coverage by assembly, class, and file.

</div>

## What's covered

- **`RequestTypeRegistry`** — JSON load, case-insensitive `(ClientId, RequestType)` lookup,
  database-overrides-JSON precedence, and inactive-row handling.
- **`SubmitRequestConsumer`** — the status-transition contract via the MassTransit in-memory test
  harness and the EF Core in-memory provider: successful completion, unknown-request-type fault,
  throwing-handler fault, missing-record skip, and redelivery idempotency.

> If the links above 404, the reports haven't been generated yet for this build. Run
> `docs/scripts/build-reports.sh` (or build the site via `src/web/build.sh`, which runs it first).
