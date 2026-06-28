#!/usr/bin/env bash
# Build the ElsaBroker web surface — the DocFX site.
#
# The site IS the documentation: conceptual articles, the generated architecture
# diagram, the ADR log, the API reference, and the browsable test/coverage
# reports. Output lands in src/web/dist (per docs/docfx.json), which the
# com.ariugwu Makefile copies to dist/elsa-broker.
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"   # .../src/web
ROOT="$(cd "$HERE/../.." && pwd)"                       # .../elsa-broker
DOCS="$ROOT/docs"

export DOTNET_ROOT="$HOME/.dotnet"   # so global-tool apphosts (docfx, reportgenerator) find .NET 9
export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

echo "==> [1/3] Architecture diagram (NetJSON -> PlantUML -> SVG, offline)"
"$DOCS/scripts/render-diagram.sh"

echo "==> [2/3] Test + coverage reports"
"$DOCS/scripts/build-reports.sh"

echo "==> [3/3] DocFX build"
docfx "$DOCS/docfx.json"

echo "==> Site built at $HERE/dist"
