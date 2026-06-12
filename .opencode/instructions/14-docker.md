# Docker & Containerization Best Practices

## Scope
Applies to: `**/Dockerfile*`, `**/docker-compose*.yml`, `**/compose*.yaml`

---

## Core Principles

1. **Immutability** — build a new image for every change; never modify running containers in production.
2. **Portability** — externalize all configuration via environment variables.
3. **Minimal attack surface** — use the smallest base image that meets requirements.
4. **Reproducibility** — pin all dependency versions; avoid `latest` tags in production.

---

## Dockerfile Rules

### Multi-Stage Builds (mandatory for compiled apps)

```dockerfile
# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["InvoiceAI.API/InvoiceAI.API.csproj", "InvoiceAI.API/"]
COPY ["InvoiceAI.Application/InvoiceAI.Application.csproj", "InvoiceAI.Application/"]
RUN dotnet restore "InvoiceAI.API/InvoiceAI.API.csproj"
COPY . .
RUN dotnet publish "InvoiceAI.API/InvoiceAI.API.csproj" -c Release -o /app/publish

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
EXPOSE 8080

# Non-root user
RUN addgroup --system appgroup && adduser --system --ingroup appgroup appuser
USER appuser

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "InvoiceAI.API.dll"]
```

### Base Image Selection
- Use `mcr.microsoft.com/dotnet/aspnet:{version}` for ASP.NET runtime.
- Use `mcr.microsoft.com/dotnet/sdk:{version}` for build stage only.
- Pin exact versions — never use `latest` in production.
- Prefer `-alpine` variants for smaller images when compatible.

### Layer Optimization
- Copy dependency files first, then source code (better cache utilization).
- Combine `RUN` commands with `&&` and clean up in the same layer.
- Never install dev tools in the runtime stage.

```dockerfile
# Good: combined and cleaned
RUN apt-get update && apt-get install -y --no-install-recommends     curl     && rm -rf /var/lib/apt/lists/*
```

### Security
- **Always run as non-root user** in production.
- Never copy secrets or credentials into the image.
- Use runtime environment variables or mounted secrets for sensitive config.
- Add `HEALTHCHECK` instruction.

```dockerfile
HEALTHCHECK --interval=30s --timeout=10s --start-period=5s --retries=3     CMD curl -f http://localhost:8080/health || exit 1
```

### .dockerignore (always include)
```
.git
.github
**/bin
**/obj
**/*.user
**/.vs
**/node_modules
**/*.md
Dockerfile*
docker-compose*
.env
.env.*
```

---

## docker-compose Rules

- Use named volumes for persistent data (PostgreSQL, uploads).
- Define explicit networks — avoid default bridge network for production.
- Set resource limits for all services.
- Use `depends_on` with `condition: service_healthy` for startup ordering.
- Never hardcode secrets in `docker-compose.yml` — use `.env` files or Docker secrets.

```yaml
services:
  api:
    build:
      context: .
      dockerfile: Dockerfile
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - ConnectionStrings__Default=${DB_CONNECTION_STRING}
    depends_on:
      postgres:
        condition: service_healthy
    networks:
      - invoiceai-network
    deploy:
      resources:
        limits:
          cpus: '1.0'
          memory: 512M

  postgres:
    image: postgres:16-alpine
    environment:
      POSTGRES_DB: ${POSTGRES_DB}
      POSTGRES_USER: ${POSTGRES_USER}
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD}
    volumes:
      - postgres_data:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U ${POSTGRES_USER}"]
      interval: 10s
      timeout: 5s
      retries: 5
    networks:
      - invoiceai-network

volumes:
  postgres_data:

networks:
  invoiceai-network:
    driver: bridge
```

---

## Checklist

- [ ] Multi-stage build used
- [ ] Pinned base image version (no `latest`)
- [ ] Non-root user defined
- [ ] `.dockerignore` present and comprehensive
- [ ] No secrets in image layers
- [ ] `HEALTHCHECK` defined
- [ ] Resource limits set in compose
- [ ] Named volumes for persistent data
- [ ] Environment variables for all configuration
