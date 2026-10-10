# delibera-webui

Browser front end for **Delibera** — a Nuxt 4 application covering the debate workflow: list debates,
create one from a registered template, watch rounds arrive live, read the verdict with token stats,
export Markdown, cancel a running debate.

This image is UI only. It talks to the API through
[`delibera-server`](https://hub.docker.com/r/techbuzzz/delibera-server) and holds no state.

---

## Quick start

```bash
docker run -d --name delibera-webui \
  -p 127.0.0.1:3000:3000 \
  -e NUXT_DELIBERA_API_BASE=http://delibera-server:8080 \
  techbuzzz/delibera-webui:latest
```

The API base URL has to be reachable **from inside the container**, so it is the service name on the
Docker network — not `localhost`, and not a host-side address.

Or bring up the full stack:

```bash
curl -O https://raw.githubusercontent.com/techbuzzz/Delibera/main/deploy/docker-compose.hub.yml
export DELIBERA_VERSION=10.5.2
docker compose -f docker-compose.hub.yml up -d
```

Then open <http://127.0.0.1:3000>.

---

## ⚠️ Security

This UI has **no authentication of its own**. It exposes everything the API exposes, including the
ability to spend LLM credits.

Every published port in the supplied compose file binds to `127.0.0.1` for that reason. Put an
authenticating reverse proxy in front before exposing this stack to any network you do not control.

---

## GitHub Pages

The project landing page lives at <https://techbuzzz.github.io/Delibera/>. The Web UI itself is
**not** deployed there — it ships in this image and is meant to run next to
[`delibera-server`](https://hub.docker.com/r/techbuzzz/delibera-server), which provides the
same-origin BFF route `/api/delibera/**`.

---

## How it reaches the API

Through a Nitro server-side proxy at `/api/delibera/**`, never directly from the browser. Two defects
in the API force this:

1. The server registers no CORS policy, so a browser calling the API origin directly is blocked on
   every request, GETs included.
2. Absolute `streamUrl` / `resultUrl` values are built from `Scheme://Host` with no
   forwarded-header handling, so they point at the wrong host behind a proxy or an ingress.

SSE is forwarded unbuffered, so a round appears the moment it is produced rather than at the end of
the debate.

---

## Configuration

| Variable | Default | Meaning |
| --- | --- | --- |
| `NUXT_DELIBERA_API_BASE` | `http://delibera-server:8080` | API base URL, resolved inside the container network |
| `NUXT_PUBLIC_TENANT_ID` | `default` | Tenant forwarded to the API |
| `NODE_ENV` | `production` | Standard Node environment |

---

## Architectures

`linux/amd64` and `linux/arm64`, published together under one manifest per tag.

## Tags

- `10.5.2`, `10.5` — pinned releases
- `latest` — newest release

## Links

- Source: https://github.com/techbuzzz/Delibera
- Release notes: https://github.com/techbuzzz/Delibera/releases

MIT licensed.