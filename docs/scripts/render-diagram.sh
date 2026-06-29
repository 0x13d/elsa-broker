#!/usr/bin/env bash
# Regenerate the architecture diagram:
#   docs/diagrams/broker.netjson  --(netjson-diagrams CLI)-->  broker.puml
#   broker.puml                   --(PlantUML, offline)------>  broker.svg
#
# Rendering is fully offline: the PlantUML MIT jar is downloaded once, SHA-256
# verified, and cached (gitignored); Smetana layout avoids any Graphviz/dot
# dependency; sprite !includes resolve from the jar's bundled stdlib. Nothing
# contacts plantuml.com / kroki.io — matching the project's no-telemetry stance.
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DOCS="$(cd "$HERE/.." && pwd)"
DIAG="$DOCS/diagrams"
CACHE="$DOCS/.cache"

# Sibling projects that own the conversions. Post-2026-06-28 community layout:
# both moved under _community/ (netjson → _community/netjson/diagrams, elsa-to-mermaid
# → _community/elsa/diagrams). Override via NETJSON_REPO / ELSA_REPO env if relocated.
NETJSON_REPO="${NETJSON_REPO:-$DOCS/../../../netjson/diagrams}"
ELSA_REPO="${ELSA_REPO:-$DOCS/../../diagrams}"

# Pinned PlantUML (MIT build). Bump version + hash together; verify the render.
PLANTUML_VERSION="${PLANTUML_VERSION:-1.2026.5}"
PLANTUML_SHA256="2e8bf02b5f4dd3fde7bca135dea6b2b319da5c1febd88f9dc6d683d240a47697"
PLANTUML_URL="https://github.com/plantuml/plantuml/releases/download/v${PLANTUML_VERSION}/plantuml-mit-${PLANTUML_VERSION}.jar"
JAR="$CACHE/plantuml-mit-${PLANTUML_VERSION}.jar"

JAVA="${JAVA:-$(command -v java || echo /opt/homebrew/opt/openjdk@17/bin/java)}"

echo "==> Generating broker.puml from broker.netjson (netjson-diagrams)"
( cd "$NETJSON_REPO" && cargo run -q -p netjson-diagrams-cli -- "$DIAG/broker.netjson" -o "$DIAG/broker.puml" )

echo "==> Ensuring PlantUML jar (${PLANTUML_VERSION}, MIT)"
mkdir -p "$CACHE"
if [ ! -f "$JAR" ]; then
  curl -fsSL "$PLANTUML_URL" -o "$JAR"
fi
echo "${PLANTUML_SHA256}  ${JAR}" | shasum -a 256 -c -

echo "==> Rendering broker.svg (Smetana, offline)"
TMP="$(mktemp -d)"; trap 'rm -rf "$TMP"' EXIT
{ echo "@startuml"; echo "!pragma layout smetana"; tail -n +2 "$DIAG/broker.puml"; } > "$TMP/broker.puml"
"$JAVA" -jar "$JAR" -tsvg -o "$TMP" "$TMP/broker.puml"
cp "$TMP/broker.svg" "$DIAG/broker.svg"
echo "==> Done: $DIAG/broker.svg"

echo "==> Rendering elsa-broker-deployment.svg (hand-authored PlantUML, offline)"
"$JAVA" -jar "$JAR" -tsvg -o "$DIAG" "$DIAG/elsa-broker-deployment.puml"
echo "==> Done: $DIAG/elsa-broker-deployment.svg"

echo "==> Generating Elsa workflow diagram (elsa-to-mermaid, fenced Mermaid)"
( cd "$ELSA_REPO" && cargo run -q -p elsa-mermaid-cli -- --fenced \
    "$DOCS/elsa/invoice-process.elsa.json" -o "$DOCS/elsa/invoice-process.mermaid.md" )
echo "==> Done: $DOCS/elsa/invoice-process.mermaid.md"
