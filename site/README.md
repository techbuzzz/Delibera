# Delibera website

The GitHub Pages site: <https://techbuzzz.github.io/Delibera/>

A [Next.js](https://nextjs.org) app configured for `output: 'export'`, following
[nextjs/deploy-github-pages](https://github.com/nextjs/deploy-github-pages). It is a *view*
over files that already live in this repository — there is no CMS and no second copy of any
content.

## What it publishes

| Page | Source |
| --- | --- |
| `/` | Version from `Delibera.Core.csproj`, release cards from `docs/WhatsNew-*.md` |
| `/docs/<slug>/` | Every Markdown file under `docs/`, at any depth |
| `/blog/<version>/` | `docs/WhatsNew-*.md` |
| `/measurements/` | `docs/performance-measurements.md` |
| `/templates/` | `src/Delibera.Core/Templates/DebateTemplate.cs` |
| `/changelog/` | `CHANGELOG.md`, split per release |

Content is read at build time. Adding a Markdown file to `docs/` adds a page — there is no
route list to update.

### Nested documents

The docs route is a catch-all, so the directory layout is mirrored in the URL:

| File | Page |
| --- | --- |
| `docs/QuickStart.md` | `/docs/quickstart/` |
| `docs/TASKS/README.md` | `/docs/tasks/` |
| `docs/TASKS/W1-correctness.md` | `/docs/tasks/w1-correctness/` |
| `docs/scope/I-00-project-vision.md` | `/docs/scope/i-00-project-vision/` |

A directory's `README.md` collapses to the directory itself, which is why the work log is
reachable at `/docs/tasks/` — the URL the previous landing page already pointed at.

Excluded, with the reason in `listDocPages()`: release notes (they are under `/blog/`),
`performance-measurements.md` (under `/measurements/`), `v10.2.6.md` (a duplicate of its
release note), and `docs/habr/` — a draft being written for Habr that still carries editorial
notes about which headline to use. Same category as `.articles/`, which is gitignored.

### Why the version comes from the csproj

`src/Delibera.Core/Delibera.Core.csproj` is the input to `dotnet pack` in
`publish-nuget.yml`. A git tag can be pushed before the package reaches nuget.org, and
CHANGELOG.md is edited by hand; either can disagree with what consumers can actually install.
The csproj cannot. If the version cannot be resolved, the build **fails** rather than
publishing a page that advertises an unpublished version.

### Why the templates page parses C#

The roles, strategy, chairman stance and round count on `/templates/` are extracted from
`DebateTemplate.cs` at build time. A hand-written list would be a second place to update on
every template change — and this project documents cases where a claim drifted from what the
code actually did. If the parse finds no templates, the page says so instead of showing a
stale list.

## Commands

```bash
npm ci
npm run dev          # http://localhost:3000
npm run build        # static export to out/
npm run verify       # check every link/asset/anchor in out/
npm run typecheck
```

`npm run build` writes to `out/`, which is what `actions/upload-pages-artifact` publishes.

## Deployment

`.github/workflows/pages.yml` runs on every merge to `main`:

1. `npm ci` in `site/`
2. `actions/configure-pages` computes the base path (`/Delibera`) and passes it as
   `PAGES_BASE_PATH`
3. `npm run build`
4. `npm run verify` — fails the deploy on a broken internal link
5. upload `site/out` and deploy

### Why `npm run verify` exists

A green `next build` proves the pages compiled, not that they link to each other. These
failures only show up on the live site:

- an internal href that no emitted file matches;
- a missing `/Delibera` prefix (Next adds `basePath` to `<Link>` and its own chunks, but
  **not** to a raw `<img src>` or a metadata URL — see `lib/paths.ts`);
- a `#fragment` pointing at a heading that was renamed;
- a route still written in the old mixed case. GitHub Pages is case-sensitive; Windows and
  local dev servers are not, so this passes locally and 404s in production.

## Layout

```
app/            routes: /, /docs, /blog, /measurements, /templates, /changelog
components/     layout shell, markdown body, table of contents
lib/content.ts  reads docs/, CHANGELOG.md, the csproj, DebateTemplate.cs
lib/markdown.ts marked + Shiki, heading anchors, repository-link rewriting
lib/routes.ts   repo path -> site route (single source of truth)
lib/paths.ts    basePath helpers for raw <img> and metadata URLs
scripts/        asset sync, export verification
```

## Adding a page

Drop a Markdown file into `docs/`. It appears in `/docs/<slug>/`, in the sitemap, and every
existing cross-link to it starts resolving.

Release notes are special-cased by filename: `docs/WhatsNew-v10.5.3.md` publishes to
`/blog/10.5.3/`. A `-RU` suffix gets its own URL rather than overwriting the English page.

## Not deployed here

The Nuxt Web UI (`src/Delibera.WebUI`) ships as the container image
`techbuzzz/delibera-webui` and runs next to `Delibera.Server`, which provides the
same-origin BFF route `/api/delibera/**`. A static export has no Nitro server and therefore
no BFF — the UI would need a separate CORS-enabled API origin, which is an owner decision
rather than something a deploy workflow should configure silently.