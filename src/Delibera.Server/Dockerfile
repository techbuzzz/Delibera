# ── Build stage ──────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore step (layer-cached)
COPY ["src/Delibera.Server/Delibera.Server.csproj", "src/Delibera.Server/"]
COPY ["src/Delibera.Core/Delibera.Core.csproj",     "src/Delibera.Core/"]
RUN dotnet restore "src/Delibera.Server/Delibera.Server.csproj"

# Build & publish
COPY . .
RUN dotnet publish "src/Delibera.Server/Delibera.Server.csproj" \
    -c Release \
    -o /app/publish \
    --no-restore

# ── Runtime stage ─────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Create non-root user
RUN adduser --disabled-password --gecos "" appuser
USER appuser

COPY --from=build --chown=appuser:appuser /app/publish .

# Debate results directory
RUN mkdir -p /app/debate_results

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
  CMD wget -qO- http://localhost:8080/api/v1/health || exit 1

ENTRYPOINT ["dotnet", "Delibera.Server.dll"]
