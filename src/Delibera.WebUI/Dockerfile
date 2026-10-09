# syntax=docker/dockerfile:1.7
# ────────────────────────────────────────────────────────────────────────────────
# Delibera WebUI — Nuxt 4 (Nitro, node-server preset)
#
# Mirrors the discipline of src/Delibera.Server/Dockerfile: an explicit healthcheck that
# runs without installing a binary for it, a non-root user, and a build stage whose
# dependency layer is cached on the lockfile alone.
# ────────────────────────────────────────────────────────────────────────────────

# ── Stage 1: dependencies ─────────────────────────────────────────────────────
# Keyed on package.json + lockfile only, so a source edit does not re-run npm ci.
FROM node:22-alpine AS deps
WORKDIR /app

# npm is pinned deliberately. node:22-alpine ships npm 12.x while a developer workstation
# commonly has 10.x, and the two resolve some transitive dependencies differently — a lockfile
# written by npm 10 fails `npm ci` under npm 12 with EUSAGE ("lock file's cac@7.0.1 does not
# satisfy cac@6.7.14"). Pinning the installer is what keeps the image build reproducible.
#
# Bump this together with the .nvmrc / local npm, never on its own.
RUN npm install -g npm@10.9.4

# npm ci (not npm install) requires a lockfile and installs exactly what it pins. Without
# a committed lockfile this stage fails outright, which is the intended guard.
COPY package.json package-lock.json ./
RUN npm ci --no-audit --no-fund

# ── Stage 2: build ────────────────────────────────────────────────────────────
FROM node:22-alpine AS build
WORKDIR /app

ENV NODE_ENV=production
ENV NPM_CONFIG_UPDATE_NOTIFIER=false

COPY --from=deps /app/node_modules ./node_modules
COPY . .

# Produces .output/ containing both the server entry point and the public assets.
RUN npm run build

# ── Stage 3: runtime ──────────────────────────────────────────────────────────
FROM node:22-alpine AS runtime
WORKDIR /app

ENV NODE_ENV=production
ENV NITRO_HOST=0.0.0.0
ENV NITRO_PORT=3000
ENV NPM_CONFIG_UPDATE_NOTIFIER=false

# node:22-alpine already ships an unprivileged `node` user (uid/gid 1000). Unlike the
# Debian-based .NET image, alpine has adduser but the base user is already correct — using
# it avoids creating a second account with the same uid.
USER node

COPY --from=build --chown=node:node /app/.output ./.output

EXPOSE 3000

# Node has a global fetch, so this needs no wget/curl — the .NET image has to install wget
# explicitly precisely because its base image lacks one (see the comment in that Dockerfile).
# NODE_ENV is already production above, which matters: Vue Router only strips its
# dev-only warnings when it is set, so leaving it unset floods the logs on unmatched routes.
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD node -e "fetch('http://127.0.0.1:3000/api/health').then(r=>process.exit(r.ok?0:1)).catch(()=>process.exit(1))"

ENTRYPOINT ["node", ".output/server/index.mjs"]