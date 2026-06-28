# ---------------------------------------------------------------------------
# ElsaBroker.Queue — multi-stage Dockerfile
#
# Build context: the solution directory (src/services/ElsaBroker/), one level
# above the Dockerfile, so the build can resolve project references
# (Contracts, Abstractions, Data) and the shared Directory.Build.props.
#
# Ports:
#   5001 — public mTLS ingress (RequireCertificate)
#   5080 — internal Elsa callback listener (shared-secret, HTTP)
#
# The server certificate (*.pfx) and CA cert are NOT baked into the image.
# Mount them at runtime via volumes or Docker secrets:
#   /app/certs/localhost.pfx   — server cert
#   /app/certs/ca.crt          — CA chain (for MtlsAuthHandler)
# ---------------------------------------------------------------------------

# ---- Build stage ----
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# 1. Copy props + csproj files first for layer-cached restore.
COPY Directory.Build.props ./
COPY ElsaBroker.Contracts/ElsaBroker.Contracts.csproj            ElsaBroker.Contracts/
COPY ElsaBroker.Abstractions/ElsaBroker.Abstractions.csproj      ElsaBroker.Abstractions/
COPY ElsaBroker.Data/ElsaBroker.Data.csproj                      ElsaBroker.Data/
COPY ElsaBroker.Queue/ElsaBroker.Queue.csproj                    ElsaBroker.Queue/

# 2. Restore (resolves the project-reference graph).
RUN dotnet restore ElsaBroker.Queue/ElsaBroker.Queue.csproj

# 3. Copy the full source for the Queue and its dependencies.
COPY ElsaBroker.Contracts/   ElsaBroker.Contracts/
COPY ElsaBroker.Abstractions/ ElsaBroker.Abstractions/
COPY ElsaBroker.Data/         ElsaBroker.Data/
COPY ElsaBroker.Queue/        ElsaBroker.Queue/

# 4. Publish release build.
RUN dotnet publish ElsaBroker.Queue/ElsaBroker.Queue.csproj \
        -c Release \
        -o /app/publish \
        --no-restore

# ---- Runtime stage ----
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

# requestTypes.json and ClientAllowlist.json are baked in via the csproj
# <Content> items. Override at runtime by bind-mounting replacements into /app/.

# mTLS ingress + internal callback listener.
EXPOSE 5001 5080

# Non-root user for defense in depth. The aspnet base image provides 'app'.
USER $APP_UID

ENTRYPOINT ["dotnet", "ElsaBroker.Queue.dll"]
