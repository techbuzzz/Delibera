# syntax=docker/dockerfile:1.7
# ────────────────────────────────────────────────────────────────────────────────
# Stage 1 — build & publish
# ────────────────────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# — Restore (layer-cached: only re-runs when .csproj files change) —
# Every referenced project must be copied here, not just the entry point: Delibera.Server
# also references Delibera.Redis, and omitting it here fails the restore with NU1101.
COPY ["src/Delibera.Server/Delibera.Server.csproj", "src/Delibera.Server/"]
COPY ["src/Delibera.Core/Delibera.Core.csproj",     "src/Delibera.Core/"]
COPY ["src/Delibera.Redis/Delibera.Redis.csproj",   "src/Delibera.Redis/"]
RUN dotnet restore "src/Delibera.Server/Delibera.Server.csproj"

# — Build & publish —
COPY . .
RUN dotnet publish "src/Delibera.Server/Delibera.Server.csproj" \
    -c "${BUILD_CONFIGURATION}" \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

# ────────────────────────────────────────────────────────────────────────────────
# Stage 2 — runtime
# ────────────────────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

# Node.js LTS — required for MCP stdio tools (npx @playwright/mcp, @marp-team/marp-cli, etc.)
# Comment out this block if you do NOT use the Operator role with stdio MCP servers.
#
# `wget` is not incidental: it is the binary the HEALTHCHECK below runs. The
# mcr.microsoft.com/dotnet/aspnet:10.0 base image ships neither wget nor curl
# (verified by inspecting the image), and the curl installed here for the NodeSource
# setup is purged again at the end of this block — so without an explicit wget the
# container reported itself unhealthy forever and anything gated on
# `service_healthy` never started.
RUN apt-get update && apt-get install -y --no-install-recommends \
        ca-certificates \
        curl \
        wget \
    && curl -fsSL https://deb.nodesource.com/setup_lts.x | bash - \
    && apt-get install -y --no-install-recommends nodejs \
    && apt-get purge -y curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app

# Create non-root user & writable directories BEFORE switching user
RUN adduser --disabled-password --gecos "" appuser \
 && mkdir -p /app/debate_results \
 && chown -R appuser:appuser /app

USER appuser

COPY --from=build --chown=appuser:appuser /app/publish .

# Runtime configuration
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
ENV DOTNET_RUNNING_IN_CONTAINER=true
ENV DOTNET_GCConserveMemory=4

EXPOSE 8080

# Health-check via the /health endpoint
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD wget -qO- http://localhost:8080/api/v1/health || exit 1

ENTRYPOINT ["dotnet", "Delibera.Server.dll"]
