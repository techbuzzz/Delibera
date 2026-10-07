# Delibera v10.5.1 — What's New

> **Release type:** patch carrying six closed issues. **No breaking changes.** Every addition is
> opt-in, and no member was added to an existing public interface — the same rule 10.5.0 followed when
> it fixed context compression.

This release closes every open GitHub issue (#11, #13, #14, #15, #16, #18) and the two findings that
were tracked only in `docs/TASKS/`. One of them was a real bug with measurable consequences.

---

## Table of contents

- [The bug that mattered](#the-bug-that-mattered)
- [pgvector was broken, and this release is what fixes it](#pgvector-was-broken-and-this-release-is-what-fixes-it)
- [Tool use](#tool-use)
- [Cost gates and rate limits](#cost-gates-and-rate-limits)
- [Debate diff](#debate-diff)
- [The CLI](#the-cli)
- [gRPC](#grpc)
- [NuGet GA: three packages](#nuget-ga-three-packages)
- [Verification](#verification)
- [Still open](#still-open)
- [Upgrading](#upgrading)

---

## The bug that mattered

### Vector-store indexing duplicated your corpus

`IndexFileAsync` assigned a fresh `Guid.NewGuid()` as every point id. Both concrete stores treat the id
as an **upsert key** — Qdrant as the point id, pgvector as `INSERT … ON CONFLICT (id) DO UPDATE` — so a
new id could never match an existing row. Re-indexing appended.

Measured in 10.5.0: three runs over 24 unique chunks left **72 points** in the collection. Every search
afterwards ranked duplicates.

```csharp
// before — three identical runs, three copies of the corpus
await rag.IndexFileAsync("docs", path);

// after — the same call three times leaves 24 points
await rag.IndexFileAsync("docs", path);
```

The id is now derived from the chunk's identity: `(collection, source_path, chunk_index)` when a source
is known, `(collection, content hash)` when it is not. Position-derived matters — an edited chunk
overwrites its own point instead of orphaning the previous one.

> ⚠️ **Migration required.** Collections written by 10.5.0 or earlier hold random-id points that
> re-indexing will never replace. Delete the collection and re-index once:
>
> ```csharp
> await vectorStore.DeleteCollectionAsync("docs");
> await rag.IndexFileAsync("docs", path);
> ```
>
> `IndexFileAsync` logs a warning when the pre-existing point count exceeds what the current document
> contributes — that is the signature of a collection that predates the fix.

---

## pgvector was broken, and this release is what fixes it

If you configure `Delibera:Rag:ProviderType = PgVector`, **this release is the one that makes it work.**

`PgVectorStore` bound the embedding as `AddWithValue(new Vector(...))`. That overload takes `object`, so
the `Vector` was boxed, Npgsql had no type to map, and the very first upsert threw:

```
InvalidCastException: Writing values of 'Pgvector.Vector' is not supported for parameters having no
NpgsqlDbType or DataTypeName
```

Search failed the same way. The embedding now travels as pgvector's own text form (`[0.5,1,-2]`) with an
explicit `NpgsqlDbType.Text` and a `::vector` cast applied by PostgreSQL — understood by any pgvector
release, so the store no longer depends on the `Pgvector`/Npgsql version pairing.

The unit tests covered this store through fakes, so it only surfaced when it was run against a real
database. **No migration is needed**: nothing previously written through `PgVectorStore` could have
succeeded, so there is no data to rescue.

Qdrant was never affected and is unchanged.

---

## Tool use

```csharp
var executor = new CouncilBuilder()
    .AddMember("gpt-4o", providerA)
    .AddMember("claude", providerB)
    .WithTools(new HttpToolProvider(httpClient, ["docs.internal.example"]))
    .WithMaxToolIterations(3)
    .Build();

var result = await executor.ExecuteAsync();
result.ToolCalls;   // what each member actually invoked
```

`IToolProvider` hands over `AIFunction` instances; the pipeline decides when to invoke them, so
argument binding, result formatting and the iteration bound live in one place rather than in every
provider.

**Two transports, one audit trail.** Members running on a provider that wraps a real `IChatClient` get
native function calling. Everything else gets the `[[TOOL: name {json}]]` marker — and that is not a
fallback for convenience.

The adapter returned by `AsChatClient()` calls `ChatAsync`, **ignores `ChatOptions.Tools`**, and
flattens every inbound message through `message.Text`. Function-call content has no text. So the
middleware never sees a request and never delivers a result: function invocation on top of that adapter
is inert. The marker protocol mirrors the Operator's existing `[[OPERATOR: …]]` convention and needs no
provider changes. Both paths write the same `ToolCallLog`, so a consumer never has to care which ran.

Ships with three providers:

| Provider | Guard rail |
|---|---|
| `FileSystemToolProvider` | rooted to one directory; rejects `../` traversal, rooted paths and alternate data streams; size-capped |
| `HttpToolProvider` | host allow-list; refuses plain HTTP by default |
| `McpToolProvider` | inherits the Operator's existing trust model |

Granting members tools grants them the tool's authority. Neither the filesystem nor the HTTP provider
is safe by accident, and neither is enabled unless you configure it.

---

## Cost gates and rate limits

```csharp
var executor = new CouncilBuilder()
    .AddMember("gpt-4o", providerA)
    .WithPricingRegistry(new ModelPricingRegistry(File.ReadAllText("prices.json")))
    .WithCostLimit(0.50m)                              // hard ceiling
    .WithRateLimit(20, TimeSpan.FromMinutes(1))         // 20 calls/min, queues beyond that
    .Build();
```

A denial does **not** throw. The debate returns a degraded result carrying the spend so far:

```
BudgetCostGate: 0.5000 spent against a limit of 0.5000.
DebateResult.CostEstimate  →  WasTruncated = true, TotalCost = 0.5012
DebateResult.IsDegraded    →  true
```

A ceiling that throws would take the debate down and leave you with no record of what was already
spent. The skipped calls show up as member failures instead, so the gap is visible rather than silent.

`CostEstimate` is flagged `IsEstimate` whenever any member's model had no registered price — an
unregistered model must not read as a free one. Token counts are the framework's own estimate; only the
provider sees the exact billed figure.

> Configuring a ceiling **without** a price list logs a warning at construction. With no prices every
> call bills zero, the gate compares zero against the limit and never denies — a ceiling that looks
> configured and is completely inert.

---

## Debate diff

```csharp
var diff = baseline.Diff(candidate);

diff.VerdictSimilarity;       // 0..1 over the whole text
diff.Rounds[0].Members[0].InlineDiff;   // "we should ship on ~~friday.~~ **monday.**"
await diff.SaveToHtmlAsync("comparison.html");
```

Rounds match on `RoundNumber` and members on display name, **never on list position**. Comparing a
four-round run against a three-round run reports the missing round instead of silently shifting every
later comparison onto the wrong response — a diff that quietly misaligns is worse than no diff.

Verdict similarity is computed over the **whole** text. `TextSimilarity` grew an explicit maximum
length parameter for exactly this: the adaptive-strategy heuristics cap comparisons at 1024 characters to
keep their cost bounded, and inheriting that cap would report two long verdicts differing only in their
final sentence as identical.

---

## The CLI

```bash
delibera run "Should we migrate off the monolith?" --rounds 3 --stream
delibera run "..." --json > baseline.json
delibera compare baseline.json candidate.json --html -o diff.html
delibera benchmark "..." -n 5
```

`compare` diffs two saved results. `benchmark` reports the distribution across runs, not a single
number — wall time is dominated by how much the models choose to write, so one run says nothing.

`resume` reconstructs a result from a checkpoint's completed rounds. It reports progress rather than
continuing the debate, and is marked `IsCompleted = false` so it cannot be mistaken for a verdict.

---

## gRPC

```csharp
builder.Services.AddGrpc();
app.MapGrpcService<DebateServiceImpl>();
```

`Delibera.Grpc` and `Delibera.Grpc.Client`, generated from `Protos/delibera.proto`, which is derived
from the contracts the server already validates.

The service layer sits **on top of** `IDebateOrchestrator`, not beside it. That is the point of having
the abstraction: distributed execution and result caching behave identically over gRPC. A parallel gRPC
pipeline would silently ignore `WithOrchestrator` and `WithCache`, and the same council would behave
differently depending on the transport.

Server streaming delivers every round exactly once and then exactly one terminal event. Iteration is a
plain `await foreach` — one `MoveNextAsync` in flight at a time — because the SSE writer shipped a bug
for precisely that reason: it compared a winner against a second `ValueTask.AsTask()` call, which
returns a *different* `Task` instance, so every genuine event was misread as a heartbeat and a debate
streamed zero rounds.

Status ordinals are pinned in the proto and covered by a test, because REST serialises the same states
as strings and nothing in either build would stop the two from disagreeing.

---

## NuGet GA: three packages

```bash
dotnet add package Delibera.Core     # the framework
dotnet add package Delibera.Server   # ASP.NET Core host
dotnet add package Delibera.Redis    # distributed execution
```

`Delibera.Server` and `Delibera.Redis` are publishable as of this release. Two details that are not
obvious: `Microsoft.NET.Sdk.Web` sets `IsPackable=false` because its default output is an app rather
than a library, and `PackageIcon` / `PackageReadmeFile` pointing at `..\..\` do not resolve during pack —
each package needs its README and icon physically next to its csproj.

`publish-nuget.yml` now packs all three and **fails the build** if any package is missing its README or
icon, because a package without them renders as a bare dependency line on nuget.org.

`Delibera.Grpc` and `Delibera.Grpc.Client` carry identical packaging metadata and are one workflow line
away from publishing. They are deliberately not in the matrix yet.

---

## Verification

| Gate | Result |
|---|---|
| `dotnet build Delibera.slnx -c Release -warnaserror` | **0 warnings, 0 errors** across 8 projects |
| `dotnet test Delibera.slnx -c Release` | **595 passed**, 0 failed, 0 skipped |
| Breakdown | 477 Core + 108 Server + 10 gRPC contract |

> **Correction to a published figure.** The 10.5.0 page for `Delibera.Core` states *514 unit tests
> (406 Core + 108 Server)*. That number was measured at 10.4.0 — 10.5.0 shipped with 10.4.0's release
> notes still on its page while the real count had moved on. It is corrected here. The 10.5.0 page
> cannot be edited.

---

## Still open

Carried forward rather than quietly dropped:

- **W2-13** — `WithCache` is missing from `ICouncilBuilder`. Adding it as an abstract member would be a
  source break, which 10.5.x does not take. Configure through `DebateExecutionOptions` instead.
- **W2-14** — on a cache hit, `TotalDuration` reports the cached debate's duration rather than the
  caller's wait time (measured: 0.0 s actual vs 189.0 s reported). A product decision, not a patch.
- **Vector pruning** — a document that *shrinks* leaves its tail chunks behind. Closing that needs a
  filter-delete on `IVectorStore`, which is breaking.
- **W4-10 channels** — the SSE writer is fixed, but `Channel.CreateUnbounded` remains in
  `LocalDebateOrchestrator` and `RedisDebateOrchestrator`, so a consumer that stops draining grows
  memory without limit.

---

## Upgrading

Nothing to do for existing code — this release has no breaking changes.

If you index documents into Qdrant or pgvector, **delete and re-index once** so the duplicate points
written before 10.5.1 are cleared.

If you configure a cost limit, also configure a price registry, or the ceiling will not be enforced.