# ---------------------------------------------------------------------------
# ElsaBroker.Processor — multi-stage Dockerfile
#
# Build context: the solution directory (src/services/ElsaBroker/), one level
# above the Dockerfile, so the build can resolve project references
# (Contracts, Abstractions, Data) and the shared Directory.Build.props.
#
# No exposed ports — this is a Worker SDK service that consumes from the
# MassTransit SQL transport queue and dispatches to Elsa workflows.
#
# The shared workflows/ folder is baked into the image at build time.
# Override at runtime via Elsa:WorkflowsPath or by bind-mounting a volume
# over /app/workflows/.
# ---------------------------------------------------------------------------

# ---- Build stage ----
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# 1. Copy props + csproj files first for layer-cached restore.
COPY Directory.Build.props ./
COPY ElsaBroker.Contracts/ElsaBroker.Contracts.csproj            ElsaBroker.Contracts/
COPY ElsaBroker.Abstractions/ElsaBroker.Abstractions.csproj      ElsaBroker.Abstractions/
COPY ElsaBroker.Data/ElsaBroker.Data.csproj                      ElsaBroker.Data/
COPY ElsaBroker.Processor/ElsaBroker.Processor.csproj            ElsaBroker.Processor/

# 2. Restore (resolves the project-reference graph).
RUN dotnet restore ElsaBroker.Processor/ElsaBroker.Processor.csproj

# 3. Copy the full source for the Processor and its dependencies,
#    plus the shared workflows folder (csproj Content item copies
#    workflow JSON files into the output under workflows/).
COPY ElsaBroker.Contracts/   ElsaBroker.Contracts/
COPY ElsaBroker.Abstractions/ ElsaBroker.Abstractions/
COPY ElsaBroker.Data/         ElsaBroker.Data/
COPY ElsaBroker.Processor/   ElsaBroker.Processor/
COPY workflows/               workflows/

# 4. Publish release build.
RUN dotnet publish ElsaBroker.Processor/ElsaBroker.Processor.csproj \
        -c Release \
        -o /app/publish \
        --no-restore

# ---- Runtime stage ----
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

# No ports — worker service only.

# Non-root user for defense in depth.
USER $APP_UID

ENTRYPOINT ["dotnet", "ElsaBroker.Processor.dll"]
