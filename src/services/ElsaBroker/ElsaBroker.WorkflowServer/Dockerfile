# ---------------------------------------------------------------------------
# ElsaBroker.WorkflowServer — multi-stage Dockerfile
#
# Build context: the solution directory (src/services/ElsaBroker/), one level
# above the Dockerfile, so the build can resolve the ../workflows Content item
# and the Directory.Build.props shared by all projects.
#
# Do NOT pin platform: linux/amd64 — the Elsa runtime runs native arm64 on
# Apple Silicon; the amd64 variant crashes under Rosetta's JIT (see ADR-0005).
# ---------------------------------------------------------------------------

# ---- Build stage ----
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# 1. Copy props + csproj first for layer-cached restore.
COPY Directory.Build.props ./
COPY ElsaBroker.WorkflowServer/ElsaBroker.WorkflowServer.csproj ElsaBroker.WorkflowServer/

# 2. Restore (only the WorkflowServer — it has no project references).
RUN dotnet restore ElsaBroker.WorkflowServer/ElsaBroker.WorkflowServer.csproj

# 3. Copy the rest of the source + the shared workflows folder.
#    The csproj <Content Include="..\workflows\**\*.json"> copies them to the
#    output directory under Workflows/.
COPY ElsaBroker.WorkflowServer/ ElsaBroker.WorkflowServer/
COPY workflows/ workflows/

# 4. Publish release build.
RUN dotnet publish ElsaBroker.WorkflowServer/ElsaBroker.WorkflowServer.csproj \
        -c Release \
        -o /app/publish \
        --no-restore

# ---- Runtime stage ----
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# The published output already contains Workflows/*.json from the csproj
# Content item. At runtime the docker-compose mount overlays /app/Workflows
# with the host's live ./workflows folder, so edits in Studio export back to
# the host without rebuilding the image.
COPY --from=build /app/publish .

# .NET 9 defaults to port 8080 via ASPNETCORE_HTTP_PORTS.
EXPOSE 8080

ENTRYPOINT ["dotnet", "ElsaBroker.WorkflowServer.dll"]
