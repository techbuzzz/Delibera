# Release process

The order below matters, and it is not the order it looks like it should be. Every step that
looks skippable has a way of failing *silently* rather than loudly, and two of them did.

Throughout: `<Version>` is the value in `src/Delibera.Core/Delibera.Core.csproj`. That file is the
single source of truth — not the tag, not `CHANGELOG.md`.

---

## Before you tag

### 1. Bump the version

```bash
# All five packaged projects, plus the Web UI manifest.
grep -rl '<Version>10\.5\.2</Version>' src --include=*.csproj
```

`Delibera.Core`, `Delibera.Server`, `Delibera.Redis`, `Delibera.Grpc`, `Delibera.Grpc.Client`,
and `src/Delibera.WebUI/package.json`.

Then release notes:

- `PackageReleaseNotes` in `Delibera.Core.csproj` and `Delibera.Server.csproj`
- `CHANGELOG.md` — convert `[Unreleased]` into `## [<Version>] - <YYYY-MM-DD>`
- `docs/WhatsNew-v<Version>.md` — hand-written; this is what the release page is built from

> ⚠️ **The tag does not set the package version.** `publish-nuget.yml` packs from the csproj. If
> you tag `v10.5.3` while the csproj still says `10.5.2`, the workflow packs `10.5.2.nupkg` and
> `dotnet nuget push --skip-duplicate` discards it without a word, because `10.5.2` already
> exists. The job goes green and nothing is published.
>
> `release.yml` now fails loudly on exactly this mismatch — but only for the release page, and
> only after the packages are already gone.

### 2. Open a PR and merge it

CI runs three gates on the PR: `build & test`, `build (verify only)`, `webui tests`. Wait for all
three. Squash-merge — the repo squash-merges, so a feature branch's SHAs never become ancestors
of `main`.

### 3. Verify before tagging

```bash
dotnet build Delibera.slnx -c Release -warnaserror
dotnet test  Delibera.slnx -c Release --no-build

# The check that actually catches the silent failure:
dotnet pack src/Delibera.Core/Delibera.Core.csproj -c Release --output ./artifacts
ls ./artifacts        # must contain <Version>.nupkg — not the previous one
```

---

## Cut the tag

```bash
git tag -a v<Version> -m "v<Version>"
git push origin v<Version>
```

Pushing `v*` starts four things in parallel:

| Workflow | What it does |
|---|---|
| `publish-nuget` | packs Core/Server/Redis, pushes to nuget.org (OIDC, no stored API key) |
| `publish-docker` | builds `linux/amd64` + `linux/arm64` for both images, pushes, then runtime-smokes them |
| `release` | creates the GitHub Release page from the WhatsNew doc + auto PR notes |
| `dockerhub-metadata` | PATCHes the Docker Hub description, overview and categories |

### Docker Hub requires two things that are easy to miss

- **The repositories must already exist.** `techbuzzz/delibera-server` and
  `techbuzzz/delibera-webui`. `docker push` into a repository that does not exist fails, and the
  workflow cannot create it.
- **`DOCKERHUB_USERNAME` and `DOCKERHUB_TOKEN` must be set** as repository secrets. Settings →
  Secrets and variables → Actions. A *scoped access token* with Read & Write — Docker Hub has
  retired classic PATs.

Missing secrets fail in **zero seconds** with `Username and password required`. That timing is
the tell: a wrong token takes about a second and says `unauthorized`.

---

## After tagging

### Check what actually landed

Green pipelines are not proof that something was published:

```bash
curl -s https://api.nuget.org/v3-flatcontainer/delibera.core/index.json | grep <Version>
curl -s https://hub.docker.com/v2/repositories/techbuzzz/delibera-server/tags | grep <Version>
```

If the nupkg pushed fine but the csproj version was stale, NuGet looks untouched and the run is
green. That is the failure mode `--skip-duplicate` creates.

### Backfilling a release page

Normally nothing to do: a merge to `main` refreshes the release page for the newest `v*` tag
reachable from `main`, so the page picks up the header and the WhatsNew document without a
handful of clicks.

Run **Actions → release → Run workflow** only to repair a *specific* tag — for instance to rebuild
the body of a release whose page was created by hand. It creates or updates; it never duplicates.

That workflow distinguishes two situations on purpose, and the difference is what keeps a routine
merge from reddening `main`:

| Trigger | Behaviour on a tag/`<Version>` mismatch |
|---|---|
| `v*` tag pushed, or dispatch naming a tag | **fails** — that tag exists to publish something specific |
| merge to `main` | **exits 0** — an unreleased version bump is a normal state, not an error |

### Backfilling Docker Hub cards

Normally also nothing to do: `dockerhub-metadata` runs on any merge to `main` that touches
`dockerhub/**` or the workflow itself.

**Actions → dockerhub-metadata → Run workflow** forces it without a code change. The cards are
empty by default when a repository is created through the API — `docker push` writes no metadata.

### Both backfills need the workflow on `main` first

`workflow_dispatch` only exists for workflows that GitHub can see on the **default branch**. A
new `release.yml` or `dockerhub-metadata.yml` sitting on a feature branch has no Actions entry,
so "Run workflow" does not merely fail — the button is not there.

Confirm what is actually dispatchable before promising a backfill:

```bash
curl -s https://api.github.com/repos/techbuzzz/Delibera/actions/workflows \
  | grep '"name"'        # only workflows on main appear here
```

The symptom if you skip this check: the tags are on Docker Hub, the pulls counter is climbing,
and the overview is still empty — with every pipeline green, because nothing in the tag-push
path writes metadata on its own.

---

## GitHub Pages (one-time setup)

**Settings → Pages → Source → GitHub Actions.** Do this **before** merging the workflow that
deploys: `pages.yml` runs on every push to `main`, and merging it into a repository whose Pages
site does not exist yet produces a red run on `main` with

```
Get Pages site failed.
Please verify that the repository has Pages enabled and configured to build using GitHub Actions
Error: Not Found
```

That failure is about the **site**, not the workflow: pushing a workflow file does not create a
Pages site, and `enablement: true` does not help either, because creating one is a repository
administration operation and the automatic `GITHUB_TOKEN` can never administer the repository — no
`permissions:` block changes that. The site is created once, by a human in Settings, or by an
authenticated `POST /repos/OWNER/REPO/pages -f build_type=workflow`.

**Do not try to diagnose this over the API.** Neither signal answers the question:

| Probe | What it says | What it does not say |
|---|---|---|
| `GET /repos/OWNER/REPO/pages` (unauthenticated) | 404 | nothing — it answers 404 for lack of permission just as readily as for a missing site |
| `"has_pages": true` on the repository | Pages is available to this repo | that a site exists: `https://OWNER.github.io/REPO/` returns a GitHub 404 while the field reads `true` |

The check that settles it is the one GitHub itself serves: does the URL answer 200?

```bash
curl -s -o /dev/null -w '%{http_code}\n' https://techbuzzz.github.io/Delibera/
```

A 404 there, served by GitHub.com itself, means nothing has ever been deployed to this Pages
site. Only the Source dropdown in Settings says whether it is configured to accept deploys.

`pages.yml` deploys the Web UI as a **static** export on every merge to `main`. Two consequences:

- A static export has **no Nitro server**, so the `/api/delibera/**` BFF route does not exist
  there. The browser needs an absolute origin via `NUXT_PUBLIC_DELIBERA_API_BASE`, and that
  origin must allow the Pages origin via CORS — which `Delibera.Server` does not do by default.
  Unset, the site deploys and its requests 404: a UI preview, not an install.
- Nuxt 4 emits to `.output/public`, **not** `dist`. GitHub's own sample workflow still uploads
  `./dist` because it predates Nuxt 4.

### Reproduce the Pages build locally

`pages.yml` runs steps that PR CI does not: `nuxt generate`, and two assertions about the export.
That gap is worth closing by hand before every Pages-related change, because none of it can fail
a pull request:

```bash
cd src/Delibera.WebUI
npm ci
npm run generate                                  # 6 routes -> .output/public
test -f .output/public/index.html                 # the gate in pages.yml
test -f .output/public/debates/new/index.html    # prerender.routes actually took

# the origin must be BAKED IN, not merely configured
NUXT_PUBLIC_DELIBERA_API_BASE=https://example.test npm run generate
grep -rqF 'https://example.test' .output/public   # 0 hits = the whole feature is dead
```

That last `grep` is the check that matters. `NUXT_PUBLIC_*` is substituted only for names declared
under `runtimeConfig.public`; an undeclared one is dropped silently, the build stays green, and the
deployed site quietly calls its own origin. It has already happened once here.

Note `npm run build` (not `generate`) before `npm test` — the SSE suite loads
`.output/server/index.mjs`, which a static export does not produce, and reports a suite failure
that looks like a broken test rather than a missing artefact.

### Verify base-path runs from PowerShell, not Git Bash

Git Bash rewrites an environment value that looks like a POSIX path when it exports it to a child
process. `NUXT_APP_BASE_URL=/Delibera npm run generate` reaches the build as
`C:/Program Files/Git/Delibera`.

That malformed base produces exactly the output a real Nuxt bug would: `.output/public/index`
and `.output/public/debates/new` containing `Redirecting...` instead of documents, no `index.html`
anywhere, and a green build. I spent a cycle "fixing" nuxt.config.ts on the strength of that.

Set these variables from PowerShell, where the value passes through untouched, before believing
anything about a base-path build:

```powershell
$env:NUXT_APP_BASE_URL = '/Delibera'
npm run generate
```

---

## Changing a workflow

Workflow files are only parsed by GitHub when they trigger — for `release` and
`dockerhub-metadata`, that is the tag push. A broken `run:` block therefore surfaces at the worst
possible moment.

Before pushing a workflow change, both of these are cheap and have each caught a real bug here:

```bash
# structure: tabs, block scalars, `on:` key
# bash syntax of every extracted run: body (Git Bash or any Linux)
bash -n <script>
```

GitHub expressions (`${{ ... }}`) must be stripped before `bash -n` — they are invalid bash and
mask every real error behind noise.

When the workflow checks out a **tag**, syntax checking is not enough. Clone shallow at that tag
and run the extracted block against the clone — a dispatch resolves `actions/checkout` to the
tag, so any file authored on `main` afterwards is simply not there:

```bash
git clone --depth 1 --branch v<Version> file:///path/to/repo /tmp/sim
cd /tmp/sim && VERSION=<Version> bash ../step.sh
```

This is how the release-notes step was caught: `cp` of a file that exists on `main` and not at the
tag is valid bash, passes `bash -n`, and fails on the only run that matters.