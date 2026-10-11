# Delibera v10.5.3 — What's New

> **Release type: feature and fix release. No breaking changes.** No public API changed, no member
> was removed from an existing interface, and no signature changed. Everything new is opt-in.

This release adds an opt-in CORS policy for deliberately static browser clients, a project website
on GitHub Pages, accessibility improvements and behaviour fixes in the Web UI, three server-side
performance fixes, and a BuiltIn constants refactor that gives every magic string a single home.

---

## Table of contents

- [The project website](#the-project-website)
- [Opt-in CORS](#opt-in-cors)
- [BuiltIn constants](#builtin-constants)
- [Server performance](#server-performance)
- [Web UI fixes and accessibility](#web-ui-fixes-and-accessibility)
- [CI and release process](#ci-and-release-process)
- [Dependency pins](#dependency-pins)
- [Verification](#verification)
- [Upgrading](#upgrading)

---

## The project website

GitHub Pages now deploys a **project website** — a Next.js static export living in `site/` —
rather than the Nuxt Web UI.

**Why the change.** The Web UI is meant to ship in the `techbuzzz/delibera-webui` container image
and run next to `Delibera.Server`, which provides the same-origin BFF route `/api/delibera/**`. A
static export has no Nitro server and therefore no BFF, so the deployed UI could only reach an API
via a separate CORS-enabled origin — a deliberate owner decision, not something the Pages workflow
should configure silently.

The site is a **view over this repository, not a copy of it**. Every page is rendered at build time
from content that already lives here:

| Page | Source |
| --- | --- |
| `/` | version from `Delibera.Core.csproj`, release cards from `docs/WhatsNew-*.md` |
| `/docs/<slug>/` | every Markdown file under `docs/`, at any depth |
| `/blog/<version>/` | `docs/WhatsNew-*.md` |
| `/measurements/` | `docs/performance-measurements.md` |
| `/templates/` | `src/Delibera.Core/Templates/DebateTemplate.cs` |
| `/changelog/` | `CHANGELOG.md`, split per release |

Consequences worth stating, because they are the point:

- **The version cannot be wrong.** It is read from the csproj that `publish-nuget.yml` actually
  packs — not from a tag (which can be pushed before the package reaches nuget.org) and not from
  this changelog (which is edited by hand). If it cannot be resolved, the build fails.
- **The templates page cannot be stale.** Roles, strategy, chairman stance and round count are
  extracted from `DebateTemplate.cs` at build time. If the parse finds nothing, the page says so
  rather than showing a list that no longer matches the package.
- **Adding a Markdown file adds a page.** No route list to update, and every existing cross-link
  to that file starts resolving. Repository-relative links are rewritten to site routes; targets
  the site does not serve become absolute GitHub links.
- **A broken cross-link fails the deploy.** `scripts/verify-export.mjs` walks the export and fails
  on an internal href with no file behind it, a missing `/Delibera` prefix, or a `#fragment`
  pointing at a renamed heading. All three pass a green `next build` and 404 in production —
  GitHub Pages is case-sensitive while every local check is not.

Code blocks are highlighted with Shiki using GitHub's own TextMate grammars, in both light and
dark themes, following `prefers-color-scheme`. The site ships no CSS framework and no client-side
JavaScript beyond the Next.js runtime.

Live at: <https://techbuzzz.github.io/Delibera/>

---

## Opt-in CORS

`Delibera.Server` gained a named CORS policy, registered only when origins are explicitly
configured.

**Off by default, and the default is the point.** The Web UI reaches the API through its own
same-origin BFF route and never needs CORS. The absent header is also what stops an arbitrary web
page from *reading* a response — a cross-origin `fetch` still fires the request without it, it just
cannot see the answer. Turning this on globally would have added a browser-side CSRF vector to an
API that is unauthenticated, unthrottled, and spends real credits per debate.

```yaml
environment:
  DELIBERA_CORS_ORIGIN: https://example.com
```

```csharp
// Program.cs — opt-in, wired only when AllowedOrigins is non-empty
builder.Services.AddDeliberaServer(builder.Configuration);
// AddDeliberaCors is called inside AddDeliberaServer; UseDeliberaCors only emits headers when a
// policy was registered.
```

Design decisions:

- **Origins are matched exactly.** There is no `SetIsOriginAllowed(_ => true)` path. This API is
  unauthenticated; "any origin" would let any page on the internet spend credits through someone
  else's browser.
- **`AllowCredentials` together with `*` is rejected at startup** with a message naming the config
  key, rather than by the framework with one that names nothing.
- **`X-Correlation-Id` is exposed** so a cross-origin caller can find its entry in the server log.

13 new tests: 8 asserting what gets registered, and 5 driving the real pipeline over HTTP — that a
default host emits no `Access-Control-Allow-Origin`, that a configured origin gets its own origin
echoed back rather than a wildcard, that an unlisted origin gets nothing, that the preflight is
answered, and that `X-Correlation-Id` is exposed.

---

## BuiltIn constants

`src/Delibera.Core/BuiltIn.cs` — a single source of truth for the magic strings that previously
drifted between `Delibera.Core`, `Delibera.Redis`, `Delibera.Server` and consumer applications.

Grouping is by domain:

| Group | Contents |
| --- | --- |
| `HttpHeaders` | `X-Correlation-Id`, `X-Tenant-Id`, `X-Accel-Buffering` |
| `HttpHeaderValues` | `text/event-stream`, `no-cache`, `application/json`, `Bearer` |
| `SseEvents` | `debate-round`, `debate-complete`, `debate-error`, `heartbeat`, `reconnect` |
| `ConfigSections` | `Delibera`, `Delibera:Server`, `Delibera:Redis`, `Delibera:Rag` |
| `Models` | Default model names and context-window patterns |
| `Endpoints` | Route prefixes and well-known paths |
| `Timeouts` | Debate, SSE heartbeat, and provider timeouts |
| `Redis` | Stream and key prefixes |
| `HttpClients` | Named `IHttpClientFactory` client names |
| `Roles` | Chairman, Knowledge Keeper, Operator display names |
| `MetadataKeys` | `HttpContext.Items` keys |
| `Json` | Serializer options preset names |
| `Paths` | Default file and directory paths |

Every constant is a `const` (or `static readonly` for non-primitive types) so the value is inlined
at compile time — there is no runtime cost and no allocation.

**SSE event names are a contract with the Web UI front-end.** Changing a value here requires a
matching change in `Delibera.WebUI`.

---

## Server performance

Three per-request costs removed:

1. **`ValidationFilter` cached validator type resolution.** It called
   `typeof(IValidator<>).MakeGenericType(arg.GetType())` for every argument of every request.
   Building a closed generic type allocates a runtime type; the mapping is now cached per argument
   type.
2. **`RedisDebateOrchestrator` reused the injected `ConnectionMultiplexer`.** It opened its own,
   bypassing the singleton the host registers — a second TCP pool, handshake and reconnect timers
   for the life of the process, for a class whose own documentation claimed it shared the connection
   with `RedisDebateCache`. It now reuses the injected multiplexer when there is one, and still
   opens its own when constructed outside DI.
3. **The SSE writer assembled each frame once.** It emitted each frame as six writes plus a flush,
   and re-encoded the event name from string to UTF-8 on every event. A frame is now assembled once
   in the response pipe and flushed once, with event names as `u8` literals.

Per-frame flushing is unchanged deliberately: a buffering proxy once passed every functional test
here while delivering rounds 150–200 seconds late.

Regression tests:

- `Validator_Type_Is_Resolved_Once_Per_Contract_Not_Per_Request` fails if the cached validator type
  disappears.
- `Round_Is_Flushed_While_The_Debate_Is_Still_Running` withholds the terminal event and requires
  the round to reach the wire within three seconds; it was verified to fail when the per-frame
  flush is removed.

`docs/performance-measurements.md` records the before/after measurement.

---

## Web UI fixes and accessibility

### The proxy kept upstream SSE connections open after the browser left

Every upstream call ran with `timeout: 0`, because a debate legitimately takes 150–200 s and its
stream stays open longer still, so nothing else ever ended those requests. Closing the tab,
navigating away or letting `EventSource` reconnect left the server-side connection running for the
remainder of the debate — one leaked stream per piece of user navigation.

The proxy now aborts its own fetch when the response closes before it was fully written, and the
resulting read error is swallowed rather than thrown: a client hanging up is not an upstream fault
worth reporting to nobody.

**Measured on the test fixture:** the upstream socket closes **6 ms** after the client disconnects
with the fix and **2699 ms** without it — the second being the moment the fake upstream ends its own
response, which is exactly the point: a connection the proxy never cancelled closes anyway.

### The typecheck was passing while silently checking less

`npm run typecheck` printed `[Vue] Load plugin failed: vue-router/volar/sfc-route-blocks` on every
run and exited **0**, so CI reported green. Two defects sat behind the one message: `vue-tsc@2`
cannot load a `vue-router@5` plugin at all, and once that is upgraded the plugin must also be
resolvable from the project root, which it is not while it only exists nested under
`node_modules/nuxt`.

Nuxt is now `4.6.1`, `vue-router@^5.4.0` matching it, `vue-tsc@3.3`, `vitest@5`,
`@nuxt/test-utils@4`, and the check is clean for the first time.

### Three Web UI behaviours that were silently wrong

- **Cancel reported itself without saying anything.** Both branches of `onCancel` called
  `loadRecord()`, so "cancelled" and "had already finished" produced identical silence and an
  unexplained status change. They now say which one happened.
- **Changing a filter did not re-query.** The selects only changed what was *selected*; the list
  kept rendering the previous filter's results under controls claiming the new one, and paging was
  never reset. A filter change now reloads from page 1.
- **`npm test` failed on a clean checkout.** The SSE suite runs the built Nitro server, and CI only
  passed because its `build` step happens to run first. `pretest` now builds.

### Accessibility

- **Rounds arriving over SSE were invisible to a screen reader.** The count in the heading changed
  silently, and the reader was most likely on the verdict panel. A polite live region now announces
  each round — polite, because rounds arrive as a steady drip and interrupting whatever is being
  read for each one would make the page unusable rather than live.
- **Every tab was titled "Delibera — AI council".** Each route now titles itself, and a debate
  names itself by its label once loaded.
- **Buttons, inputs and rows gained hover states and a visible `:focus-visible` ring**, the browser
  default being near-invisible against these dark surfaces.
- **The error page now shows the status message** and clears the error through `clearError`
  instead of relying on a link that renders without the router state it needs.

### Proxy stays on `h3`, and says why

Nuxt 4.6 added `nuxt/server`, a server-agnostic surface that re-declares the request helpers
against Nuxt's own `RequestEvent` and omits the response-writing helpers, so a non-Nitro builder
can satisfy it. The proxy route is exactly the case it cannot serve: forwarding an upstream body
verbatim is `sendWebResponse`, streaming SSE chunk by chunk is `sendStream`, and Nuxt's
`RequestEvent` is not assignable to h3's `H3Event` — mixing the surfaces fails typecheck, not at
runtime. The imports stay on `h3` with the reasoning recorded at the import.

### Lockfile fix

The Web UI `package-lock.json` was out of sync with `package.json` under npm 10 — `unplugin@3.4.0`
peer dependency entries were missing. Regenerated with `npm@10.9.4` matching the Dockerfile pin.
The Docker build (`npm ci`) now succeeds.

---

## CI and release process

- **Docker Hub publish is behind an environment** (`dockerhub-prod`), like NuGet (`nuget-prod`).
  Both require approval before pushing to the registries.
- **Release pages are built from `docs/WhatsNew-*.md`** — the release workflow creates the GitHub
  Release page from this document and an auto-generated PR list, and fails loudly on a
  tag/version mismatch.
- **Merge commits instead of squash.** Squash was what made branch handling expensive: the squashed
  PR's commits are not ancestors of `main`, so `git merge-base --is-ancestor <branch> main` cannot
  answer whether the work shipped, and merging `main` back into a long-lived feature branch produces
  a disconnected merge rather than a fast-forward.
- **`dockerhub-metadata` workflow** PATCHes the Docker Hub repository description, overview and
  categories on every merge to `main` that touches `dockerhub/**`.

See `docs/ReleaseProcess.md` for the full release runbook.

---

## Dependency pins

### Node is pinned to an exact patch

`nuxt@4.6.1` declares `engines.node: ^22.22.3 || ^24.15.0 || >=26.0.0`, so the floating
`node:22-alpine` and `.nvmrc: 22` that worked by accident stop working the moment the tag moves
against us. Both are now `22.23.3`, the Dockerfile resolving one `ARG NODE_VERSION` across its
three stages.

Node 24 ("Krypton") satisfies the range too and is the natural move before 22 ("Jod") leaves
maintenance in April 2027 — but that also moves the bundled npm from 10.x to 11.x, which deserves
its own change.

### npm is pinned in the Dockerfile

The base image ships npm 10.9.9 while a developer workstation commonly has a different major, and
the two resolve some transitive dependencies differently. The Dockerfile pins `npm@10.9.4` so the
image build is reproducible. Bump this together with the `.nvmrc` / local npm, never on its own.

---

## Verification

Verified on this tree:

```bash
dotnet build Delibera.slnx -c Release -warnaserror   # 0 errors / 0 warnings
dotnet test  Delibera.slnx -c Release               # Core + Server + gRPC tests passing
dotnet pack src/Delibera.Core/Delibera.Core.csproj -c Release   # emits 10.5.3.nupkg

# Web UI
cd src/Delibera.WebUI
npm ci --no-audit --no-fund       # succeeds (lockfile in sync under npm 10)
npm test                          # API contract + SSE streaming tests
npm run typecheck                 # clean — no plugin load failures

# Site
cd site
npm ci
PAGES_BASE_PATH=/Delibera npm run build
npm run verify                     # every internal link, asset and anchor resolves
```

---

## Upgrading

**No migration is needed.** Nothing in the public API changed, and no default behaviour changed.

What is new and opt-in:

| Feature | How to enable |
| --- | --- |
| CORS for static browser clients | Set `Delibera:Server:Cors:AllowedOrigins` to a list of origins |
| Project website | Already live at <https://techbuzzz.github.io/Delibera/> |
| BuiltIn constants | Reference `Delibera.Core.BuiltIn.*` instead of copying literals |
| Node 22.23.3 | Update `.nvmrc` and the Dockerfile `ARG NODE_VERSION` together |

Set `DELIBERA_VERSION=10.5.3` to pin the Docker images; omit it to track `:latest`.

---

## What's next

- **Node 24 ("Krypton")** — the natural move before 22 ("Jod") leaves maintenance in April 2027.
  Moves the bundled npm from 10.x to 11.x, which is a separate change to make on purpose.
- **C# 15 union types and closed hierarchies** — deferred to the `net11.0` retarget; they need
  `System.Runtime.CompilerServices.IUnion` and `IsClosedTypeAttribute`, which ship with .NET 11.
  The full feature gate is recorded in `docs/NET10-Upgrade.md`.
- **Distributed turn execution** — `DebateWorkerService.ProcessMessageAsync` remains a deliberate
  no-op. Redis today buys shared state and round events, not horizontal debate scaling.

---

1. **Pull.** `git pull` on `main`; the tag is `v10.5.3`.
2. **NuGet.** `Delibera.Core`, `Delibera.Server` and `Delibera.Redis` are all `10.5.3`.
3. **Docker.** `techbuzzz/delibera-server:10.5.3` and `techbuzzz/delibera-webui:10.5.3`,
   `linux/amd64` + `linux/arm64`.
4. **Site.** <https://techbuzzz.github.io/Delibera/> shows `10.5.3`.
