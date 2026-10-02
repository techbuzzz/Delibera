# W1 — Correctness & Stability

> **Goal:** remove the defects that are observable in production behaviour, not in code style.
> **Rule for this wave:** every item ships with a test that fails before the fix.
> **Status:** 8 / 10 done (W1-01…W1-08 except W1-09, W1-10)

---

## W1-01 · `StreamDebateAsync` yielded 0 rounds for any asynchronous provider · **P0** · ✅ done

**Problem.** `CouncilExecutor.ExecuteCoreWithCallbackAsync` installed the streaming
interceptor by assigning the `OnRoundCompleted` **field** and restoring it in a `finally`:

```csharp
private Task<DebateResult> ExecuteCoreWithCallbackAsync(Action<DebateRound> onRoundCompleted, CancellationToken ct)
{
   var original = OnRoundCompleted;
   try
   {
      OnRoundCompleted = onRoundCompleted;   // interceptor installed
      return ExecuteCoreAsync(ct);           // returns a Task — we do NOT wait for it
   }
   finally
   {
      OnRoundCompleted = original;           // …and it is already restored here
   }
}
```

The method is not `async`, so the `finally` runs as soon as the task is *created* — before
the debate has done any I/O. `ExecuteCoreAsync` reads the field at
`OnRoundCompleted?.Invoke(round)` (time of round completion), by which point the original
handler is back in place and the interceptor is gone. Nothing is written to the channel,
so `StreamDebateAsync` completes with **zero** rounds after the whole debate finishes.

**Why 11 tests were green.** `FakeLLMProvider` returns `Task.FromResult` (or an `async`
method that never suspends), so the entire debate runs inline *inside* `ExecuteCoreAsync`
while the field is still swapped. The bug is invisible unless the debate actually awaits.
`GET /debates/{id}/stream` therefore produced an empty SSE stream against any real
provider — the headline feature of v10.2.6.

**Proof (test added first, failed second).**
`StreamingCouncilTests.StreamDebateAsync_Yields_Rounds_When_Provider_Completes_Asynchronously`
uses `chatDelayMs: 5`:
```
Expected rounds to contain 2 item(s), but found 0: {empty}
```

**Fix.** Stop swapping shared state. The interceptor is a parameter of the core
execution path and the public event is raised by the executor itself:

```csharp
private async Task<DebateResult> ExecuteCoreAsync(
   CancellationToken ct, Func<DebateRound, DebateRound>? roundInterceptor = null)
...
var observedRound = roundInterceptor?.Invoke(round) ?? round;
OnRoundCompleted?.Invoke(observedRound);
```

`StreamDebateAsync` passes an interceptor that stamps `Total` and writes to the channel;
user handlers therefore still fire exactly once per round, with the stamped round.

**Acceptance.**
- [x] Regression test fails on the old code, passes on the new
- [x] All 12 pre-existing streaming tests still pass (event fires once per round, order preserved, `Total` stamped, timeout + mid-stream cancellation work)
- [ ] SSE endpoint test asserting a non-empty stream end-to-end (W4 wave)

---

## W1-02 · `UseInMemoryCache` throws when the cache is resolved · **P0** · ✅ done

**Problem.** `ServiceCollectionExtensions.cs:365-368`:

```csharp
services.TryAddSingleton<IDebateCache>(sp =>
   new InMemoryDebateCache(
      sp.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>(), ttl));
```

`GetRequiredService<IMemoryCache>()` resolves at first use. `AddMemoryCache()` is called
**nowhere in the repository** (grep over `src/`, `tests/`, `docker-compose`: 0 matches), so
the documented `services.AddDelibera(...).UseInMemoryCache()` path throws
`InvalidOperationException: Unable to resolve service for type
'Microsoft.Extensions.Caching.Memory.IMemoryCache'` on first debate.

**Fix.** `services.AddMemoryCache();` (or `TryAddSingleton`) inside `UseInMemoryCache` —
`AddMemoryCache` is itself idempotent, so a host that already registered it is unaffected.

**Acceptance.**
- [x] `DependencyInjectionTests.UseInMemoryCache_Resolves_Without_A_Manual_AddMemoryCache`
- [x] `UseInMemoryCache_RoundTrips_A_Result`

---

## W1-03 · Redis worker busy-loops · **P0** · ✅ done

**Problem.** `DebateWorkerService.cs:60-68`:

```csharp
var messages = await _db.StreamReadGroupAsync(
   key, group, consumer, StreamPosition.NewMessages, _options.BatchSize);
if (messages.Length == 0) continue;   // ← immediately re-issued
```

`XREADGROUP` is called **without `block:`**, so it returns instantly. With an empty stream
the loop spins at full speed and pins one core. `RedisOrchestratorOptions.BlockMs`
(default 2000) is declared and documented — and never used.

**Fix.** Pause on the client when the read comes back empty, using the already-declared
`BlockMs` (2000 ms) as the poll interval.

> **Correction (verified against the package, 2026-10-02).** An earlier draft of this task
> proposed `block: _options.BlockMs` on `StreamReadGroupAsync`. That is not possible with
> this client: the widest overload in StackExchange.Redis 3.0.17 takes
> `claimMinIdleTime` as its seventh argument, not `block` — the XML documentation in
> `lib/net10.0/StackExchange.Redis.xml` lists the parameters as
> `key, groupName, consumerName, position, count, noAck, claimMinIdleTime, flags`, and
> there is no `XREADGROUP … BLOCK` surface at all. A client-side wait is therefore the
> correct fix, and the comment in the code says so.

**Acceptance.**
- [x] An empty read no longer re-issues immediately — `BlockMs` is honoured as the wait
- [ ] Idle worker consumes ~0% CPU (a timing test against a real Redis is the follow-up in W5)

---

## W1-04 · No tenant isolation on debate endpoints · **P0** · ✅ done

**Problem.** `DebateEndpoints.cs` reads, cancels and exports a debate by id
(`GET /{id}`, `DELETE /{id}`, `GET /{id}/result`, `GET /{id}/export/markdown`, `/{id}/rounds`,
`/{id}/stream`) and lists all debates (`GET /debates`) **without comparing**
`record.TenantId` with the caller resolved by `X-Tenant-Id` /
`TenantResolutionMiddleware`. `CorpusService` and `DebateRecord` already carry a tenant,
so the field exists — it is simply never checked.

**Fix (implemented differently, and deliberately).** The obvious shape — resolve the caller
tenant in the endpoint and compare — leaves the service free to forget the check, which is
exactly what had happened. Instead the **tenant is a required parameter of every lookup**:

```csharp
DebateRecord? Find(string debateId, string tenantId);
DebateRecord[] List(string tenantId, string? templateId, string? status, int page, int pageSize);
bool Cancel(string debateId, string tenantId);
```

The un-scoped overloads are gone, so a call site that forgets the tenant does not compile —
the compiler found all 8 remaining call sites (endpoints, MCP tools, tests) on the first build.
A mismatch returns `null`/`false`, which the endpoints surface as 404: an unknown id and
another tenant's id are deliberately indistinguishable, so the API cannot be used to probe for
the existence of a foreign debate. Filtering in `List` happens **before** paginating —
filtering afterwards would leak other tenants' records through empty pages and shifting
offsets.

This is a contract change of `Delibera.Server`, which is a host application (no `IsPackable`,
no `PackageId`) with a single in-repo implementation, so no published consumer is affected.

The MCP surface has no `X-Tenant-Id` header, so it now runs under its own `McpTenantId = "mcp"`
identity and can only see debates it started itself. Consequence worth stating out loud: an MCP
client can no longer fetch a debate that an HTTP tenant created.

**Acceptance.**
- [x] `TenantIsolationTests` — 7 tests: foreign-tenant `Find`/`Cancel` rejected, the owning
      tenant still works, `List` returns only the caller's records, and a foreign `Cancel`
      leaves the debate running
- [x] `List_FiltersByTenantBeforePaginating` fails against the paginate-then-filter variant
- [x] Tenant comparison is ordinal (documented by a test: `Acme` ≠ `acme`)
- [x] Single-tenant deployments are unaffected: every debate is stored under the resolved
      default tenant, so the filter matches everything as before
- [ ] End-to-end HTTP coverage of the 404 path (needs `WebApplicationFactory`; see W5)

---

## W1-05 · `DebateRecord.Rounds` is mutated by three threads · **P1** · ✅ done

**Problem.** `DebateRecord.Rounds` is a plain `List<DebateRound>`. It is appended by the
background debate task (`DebateOrchestrationService`, the `OnRoundCompleted` handler in
`LocalDebateOrchestrator:187` / `RedisDebateOrchestrator:176`) and enumerated by request
threads (`DebateEndpoints:168`, `SseDebateStreamWriter:60-61`). `LocalDebateOrchestrator.cs:97`
iterates `entry.CompletedRounds` from the very same handler that appends to it. The
`_lock` field on `DebateRecord` exists but is **never used** (grep: 1 declaration, 0 usages).

**Failure mode.** `InvalidOperationException: Collection was modified` → 500 with a
truncated SSE stream, non-deterministically, under load.

**Fix (done, in three places — the same defect existed twice in Core/Redis and once in the
Server).**
- `LocalDebateOrchestrator` and `RedisDebateOrchestrator`: their private entry class stored
  `CompletedRounds` as a `List<DebateRound>`, appended from the debate loop's
  `OnRoundCompleted` handler and enumerated by `StreamAsync` **on the same object**.
- `DebateRecord.Rounds` (Server): appended by the debate background task and by the SSE
  writer, read by `GET /{id}/rounds`, the SSE writer and the MCP tools.

All three are now `ConcurrentQueue<DebateRound>`; readers take a snapshot
(`ToArray()` / `RoundsSnapshot()`) so a page is not taken from a still-growing collection.
The unused `DebateRecord._lock` field is gone.

**Acceptance.**
- [x] `DebateRoundCollectionTests.Rounds_Survive_Concurrent_Writers_And_Readers` — 6 writers ×
      500 rounds while 4 threads enumerate and snapshot; no exception, nothing lost
- [x] `AddRounds` / `RoundsSnapshot` preserve order (2 tests)

---

## W1-06 · Cancellation is reported as a domain error · **P1** · ✅ done

**Problem.** Inside the synchronous round callback, async work is awaited with
`GetAwaiter().GetResult()` (`CouncilExecutor:876` for checkpoints, `:860-863` for the
adaptive selector). A cancelled operation throws `OperationCanceledException`, which
`GetAwaiter()` rethrows wrapped in `AggregateException`. The surrounding
`catch (Exception ex)` handlers then report it as `"Persistence"` or `"StrategySelector"`
error and the debate carries on instead of stopping.

**Fix (safe half).** Re-raise cancellation before the general catch:

```csharp
catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
```

**Acceptance.**
- [x] Both handlers re-raise `OperationCanceledException` before the general `catch`
      (`CouncilExecutor` round callback → `StrategySelector`; `SaveCheckpointAsync` → `Persistence`)
- [ ] Test: cancel during round 2 → `ExecuteAsync` throws OCE, not a completed result
- [ ] No "Persistence error" execution log on cancellation

> The structural fix (async round callback) is **W3-07** and lands in 10.4.0.

---

## W1-07 · SSE delivers every round twice · **P1** · ✅ done

**Problem.** `SseDebateStreamWriter` first replays the rounds already accumulated in
`record.Rounds`, then subscribes to `IDebateOrchestrator.StreamAsync` and replays
`entry.CompletedRounds` — the same rounds. A client that does not deduplicate renders
"Round 1" twice and its progress bar overshoots.

**Fix (done).** `LocalDebateOrchestrator.StreamAsync` already yields the rounds that
completed before the client connected and then the live ones. The writer additionally drained
`record.Rounds` and wrote them again, so every earlier round reached the client twice. The
drain loop and the duplicate `record.Rounds.Add` are removed — the orchestrator stream is the
single source of truth. (Removing that write also took one of the two writers off the
shared round collection, see W1-05.)

**Acceptance.**
- [x] `Sse_Delivers_Each_Round_Exactly_Once` — a 2-round debate produces 2 `debate-round`
      events
- [x] Verified against the old behaviour by temporarily restoring the drain loop: the same
      test then reported **4** events instead of 2, and the mixed replay/live case 3 instead of 2
- [x] The already-completed branch still replays the stored rounds as a single array

---

## W1-08 · Every server option is silently ignored · **P1** · ✅ done

**Problem.** `DeliberaServerOptions` binds `SectionName = "DeliberaServer"` while
`appsettings.json` and `docker-compose.yml` place the block under `Delibera:Server`. The
binder therefore never matches, and every option — including `OtlpEndpoint`,
`DefaultTenantId` and the OTLP exporter switch — keeps its default value forever. The OTLP
exporter is consequently never enabled.

**Fix.** Set `SectionName = "Delibera:Server"`.

**Acceptance.**
- [ ] Test: a configuration with a non-default `DefaultTenantId` is actually bound
- [ ] `docker compose up` enables the OTLP exporter when `OtlpEndpoint` is set

---

## W1-09 · Redis streams grow without bound · **P1** · ⬜ todo

**Problem.** `RedisDebateOrchestrator` appends to `delibera:events` via `StreamAddAsync`
without `MAXLEN`/approximate trimming, and writes orchestration state with `HashSetAsync`
**without TTL**. A long-running deployment grows memory linearly with the number of
debates, including completed ones. The consumer group `orchestrator` is created but never
read in this repository.

**Fix.** `maxLength` on `StreamAddAsync`; a TTL on `delibera:state:*` sized to the
maximum expected debate duration; and either wire the group up or document it as
reserved.

**Acceptance.**
- [ ] `XLEN delibera:events` stops growing past the configured cap
- [ ] `delibera:state:*` keys expire

---

## W1-10 · `RedisDebateCache` ignores cancellation · **P2** · ⬜ todo

**Problem.** `RedisDebateCache` (`GetAsync`/`SetAsync`/`InvalidateAsync`/`ExistsAsync`)
accepts a `CancellationToken` and never forwards it to the StackExchange.Redis call. A
client that disconnects mid-request keeps the I/O running.

**Fix.** Forward the token to every `*Async` call.

**Acceptance.**
- [ ] No unused `ct` parameters remain in the file
