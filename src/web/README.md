# src/web — ElsaBroker web surface

The ElsaBroker landing page **is** its documentation site, built with [DocFX](https://dotnet.github.io/docfx/).
There is no separate front-end app — the "web project" is the DocFX build.

## Build

```bash
./build.sh
```

This runs three steps and emits the static site into `dist/`:

1. **Diagram** — regenerates the architecture diagram from `docs/diagrams/broker.netjson` via the
   sibling `netjson-diagrams` CLI, then renders it to SVG offline (`docs/scripts/render-diagram.sh`).
2. **Reports** — runs the test suite and produces the HTML test + coverage reports
   (`docs/scripts/build-reports.sh`).
3. **DocFX** — generates API reference from the C# projects and builds the full site
   (`docs/docfx.json`, output configured to `src/web/dist`).

## Where things live

- **Content** (articles, ADRs, test-results page, NetJSON example) → `../../docs/`
- **DocFX config** → `../../docs/docfx.json`
- **Build output** → `dist/` (git-ignored)

## Publish

`make` at the `com.ariugwu` root runs `build-broker`, which executes this script and copies `dist/` to
`com.ariugwu/dist/elsa-broker` (served at `https://ariugwu.com/elsa-broker`).

## Requirements

- .NET 9 SDK on `PATH` (under `~/.dotnet` here), plus the `docfx` and `dotnet-reportgenerator-globaltool`
  global tools (`dotnet tool install -g docfx dotnet-reportgenerator-globaltool`).
- A JDK (for the offline PlantUML render) and the `netjson-diagrams` sibling repo for the diagram step.
