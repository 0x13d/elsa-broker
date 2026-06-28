# ---------------------------------------------------------------------------
# ElsaBroker.Switchboard — multi-stage Dockerfile
#
# Build context: the solution directory (src/services/ElsaBroker/), one level
# above the Dockerfile, so the build can resolve project references
# (Abstractions) and the shared Directory.Build.props.
#
# Port:
#   5002 — Blazor Server dashboard + /api/events ingestion endpoint
# ---------------------------------------------------------------------------

# ---- Build stage ----
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# 1. Copy props + csproj files first for layer-cached restore.
COPY Directory.Build.props ./
COPY ElsaBroker.Abstractions/ElsaBroker.Abstractions.csproj      ElsaBroker.Abstractions/
COPY ElsaBroker.Switchboard/ElsaBroker.Switchboard.csproj        ElsaBroker.Switchboard/

# 2. Restore.
RUN dotnet restore ElsaBroker.Switchboard/ElsaBroker.Switchboard.csproj

# 3. Copy source.
COPY ElsaBroker.Abstractions/ ElsaBroker.Abstractions/
COPY ElsaBroker.Switchboard/  ElsaBroker.Switchboard/

# 4. Publish release build.
RUN dotnet publish ElsaBroker.Switchboard/ElsaBroker.Switchboard.csproj \
        -c Release \
        -o /app/publish \
        --no-restore

# ---- Runtime stage ----
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

EXPOSE 5002

USER $APP_UID

ENTRYPOINT ["dotnet", "ElsaBroker.Switchboard.dll"]
