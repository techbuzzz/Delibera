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

If a tag already exists and no release page does, run **Actions → release → Run workflow** and
set the `tag` input. It creates or updates; it never duplicates.

### Backfilling Docker Hub cards

Same for **dockerhub-metadata → Run workflow**. The cards are empty by default when a repository
is created through the API — `docker push` writes no metadata. The overviews live in
`dockerhub/*.md` and are synced from there.

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

**Settings → Pages → Source → GitHub Actions.** The API returns 404 until this is set, and
creating it needs an admin token — it is not automatable from CI.

`pages.yml` deploys the Web UI as a **static** export on every merge to `main`. Two consequences:

- A static export has **no Nitro server**, so the `/api/delibera/**` BFF route does not exist
  there. The browser needs an absolute origin via `NUXT_PUBLIC_DELIBERA_API_BASE`, and that
  origin must allow the Pages origin via CORS — which `Delibera.Server` does not do by default.
  Unset, the site deploys and its requests 404: a UI preview, not an install.
- Nuxt 4 emits to `.output/public`, **not** `dist`. GitHub's own sample workflow still uploads
  `./dist` because it predates Nuxt 4.

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