#!/usr/bin/env bash
# Produce the browsable test + coverage reports that the DocFX "Test Results"
# section embeds:
#   docs/test-results/tests/index.html      (xUnit HTML logger)
#   docs/test-results/coverage/index.html   (ReportGenerator from Coverlet/Cobertura)
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DOCS="$(cd "$HERE/.." && pwd)"
SLN_DIR="$DOCS/../src/services/ElsaBroker"
OUT="$DOCS/test-results"
WORK="$DOCS/.cache/test-run"

export DOTNET_ROOT="$HOME/.dotnet"   # so global-tool apphosts find the .NET 9 runtime
export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

rm -rf "$WORK" "$OUT/coverage" "$OUT/tests"
mkdir -p "$WORK" "$OUT/coverage" "$OUT/tests"

echo "==> Running tests (HTML + TRX + Cobertura coverage)"
( cd "$SLN_DIR" && dotnet test \
    --collect:"XPlat Code Coverage" \
    --logger "html;LogFileName=test-results.html" \
    --logger "trx;LogFileName=test-results.trx" \
    --results-directory "$WORK" )

COBERTURA="$(find "$WORK" -name 'coverage.cobertura.xml' | head -n1)"
echo "==> Generating coverage HTML from $COBERTURA"
reportgenerator \
  "-reports:$COBERTURA" \
  "-targetdir:$OUT/coverage" \
  "-reporttypes:Html;Badges" \
  "-title:ElsaBroker coverage" >/dev/null

cp "$WORK/test-results.html" "$OUT/tests/index.html"
echo "==> Reports written to $OUT/{tests,coverage}"
