# W2 — Core Performance

> **Goal:** remove the costs that sit on the per-round and per-participant path of a debate.
> **Measurement rule:** no task ships without either a benchmark number or a stated
> argument for why the cost cannot matter. Static claims are labelled as such.
> **Status:** 7 / 9 done (all but W2-05 and W2-07)

Shared hot path: every round, `DebateScenario` fans out one `ChatAsync` per participant
(`Task.WhenAll` at `DebateScenario.cs:71`), estimates tokens, and writes an execution-log
entry per response. Anything added inside that fan-out is multiplied by
`participants × rounds`.

---

## W2-01 · `TokenCounter` takes a write lock on every cache *hit* · **P0** · ✅ done

**Problem.** `TokenCounter.EstimateTokens` (`Compression/TokenCounter.cs:76-83`):

```csharp
if (_memo.TryGetValue(text, out var cached))
{
   TouchMemoEntry(text);   // → EnterWriteLock + LinkedList.Remove + AddLast
   return cached;
}
```

`TouchMemoEntry` (`:176-188`) takes `_lruLock.EnterWriteLock()` and does
`LinkedList.Remove` — an O(n) walk of a list capped at 1024 entries. The read path of a
memoisation cache takes an **exclusive** lock, and `TokenCounter.Default` is a **process-wide
static** shared by every debate in the process. Since participants are fanned out with
`Task.WhenAll`, all of them serialise on this one lock for every prompt they measure.

**Fix (implemented as CLOCK / second-chance).** The memo now stores a `MemoEntry` carrying the
value and a `Used` flag, backed by a `ConcurrentDictionary` plus a `ConcurrentQueue` of
insertion order:

```csharp
if (_memo.TryGetValue(text, out var cached))
{
   cached.Used = true;   // flag write only — no lock, no O(n) list walk
   return cached.Value;
}
```

Eviction walks the queue; an entry whose flag is set gets a second chance (flag cleared,
pushed to the back), an unflagged one is removed. A hit therefore costs one dictionary lookup
and one bool write, and the `ReaderWriterLockSlim` is gone from the class entirely. The
`Used` write is deliberately racy: a lost or duplicated write only makes the eviction estimate
slightly less precise, which is not observable for a token heuristic.

Two details that a naive port gets wrong, and that the tests pin:
- **The queue must be bounded.** A second-chance re-queue adds an item without removing one,
  so uniformly hot keys would lengthen the queue forever. `TrimMemoOrder` rebuilds it once it
  passes twice the capacity, keeping every key still in the memo so nothing becomes
  permanently un-evictable; a lost `CompareExchange` just retries next time.
- **The scan must be budgeted.** A cache full of hot keys would otherwise spin inside the
  eviction loop, and stale queue entries must not consume the removal budget.

`InternalsVisibleTo("Delibera.Core.Tests")` was added so the tests can observe `MemoizedCount`
and `EvictionQueueLength` — the capacity bound is documented behaviour, and it cannot be
protected without being visible.

**Acceptance.**
- [x] No `ReaderWriterLockSlim` and no `EnterWriteLock` anywhere in `TokenCounter`
- [x] Eviction respects `MaxMemoizedEntries` — `Memoization_Stays_Within_MaxMemoizedEntries`
      floods 500 distinct fragments into a cache of 8
- [x] Queue cannot grow without bound under an all-hot workload —
      `Eviction_Queue_Does_Not_Grow_Without_Bound`
- [x] Hot keys are not eagerly evicted (`Hot_Keys_Are_Not_Eagerly_Evicted`)
- [x] Concurrency: 8 workers × 2 000 estimates, no exceptions, estimates identical to the
      un-memoised values (`Concurrent_Estimates_Are_Safe_And_Correct`)
- [ ] Microbenchmark of the hit path under contention (BenchmarkDotNet; the lock is gone, so
      this is now a confirmation rather than a blocker)

---

## W2-02 · Knowledge Keeper performs the same RAG search twice per round · **P0** · ✅ done

**Problem.** `KnowledgeKeeper.ProvideContextForRoundAsync`:

```csharp
var searchResults = await _ragProvider.SearchAsync(CollectionName, query, limit, ct: ct);   // :165
...
var contextText    = await _ragProvider.GetContextAsync(CollectionName, query, limit, ct);    // :185
```

and `BaseRagProvider.GetContextAsync` (`:111`) starts with
`var results = await SearchAsync(collectionName, query, limit, ct: ct);`.

Identical collection, identical query, identical limit. Each call embeds the query and
walks the vector store, so every round with a Knowledge Keeper pays **double** the
embedding + vector-search cost, and the second result is thrown away in favour of a
string built from the first one.

**Fix.** Build `contextText` from the `searchResults` already in hand (the same loop
`BaseRagProvider.GetContextAsync:116-121` performs), and call a shared formatter so the
two paths cannot drift apart again.

**Acceptance.**
- [x] `KnowledgeKeeperRagTests` counts calls: exactly one `SearchAsync` per round, zero `GetContextAsync`
- [x] Retrieved evidence still reaches the model prompt (asserted on the recorded prompt)

---

## W2-03 · Full store scan on every round when checkpointing · **P1** · ✅ done

**Problem.** `CouncilExecutor.SaveCheckpointAsync` (`:1122-1133`) runs *per round* and,
when no resume id is set, calls `store.ListAsync(ct)` and then
`list.FirstOrDefault(m => m.OriginalQuestion == _context.UserPrompt)`. That is a full read
of every checkpoint (each containing all rounds) plus a linear scan, executed only to find
an id that cannot change during the debate.

**Fix (done).** The id is resolved on the **first** checkpoint and then carried through the
rest of the debate in a per-execution `CheckpointTarget` holder (a local, so two debates on
one executor cannot share it). The first save may be the one that assigns the id, and
`target.Id ??= id` captures it for the rounds that follow.

The observable behaviour is unchanged, and the tests prove the equivalence rather than
asserting it: on the old code a 3-round debate called `ListAsync` 3 times, and the round
"all rounds write to the same checkpoint id" test passed there too — because the per-round
scan used to rediscover the checkpoint the previous round had just written. The scan was
re-deriving a value it already knew.

**Acceptance.**
- [x] `Fresh_Debate_Resolves_The_Store_Only_Once` — 3 rounds, `ListAsync` called exactly once
- [x] `Resumed_Debate_Loads_The_Resume_Id_Only_Once` — 2 rounds, `LoadCheckpointAsync` once,
      no `ListAsync` (was 2 loads)
- [x] `Every_Round_Overwrites_The_Same_Checkpoint` — all rounds share one id
- [x] `A_New_Debate_Does_Not_Inherit_A_Previous_Checkpoints_Id`
- [x] Regression guard checked by stashing the old `CouncilExecutor` and re-running: the
      count assertions fail (3 instead of 1, 2 instead of 1)

---

## W2-04 · Response diversity is O(n²) Levenshtein over full texts · **P1** · ✅ done

**Problem.** `ComputeResponseDiversity` (`CouncilExecutor:1076-1094`) builds every response
pair and calls `TextSimilarity` → `LevenshteinDistance`, a full O(lenA × lenB) double loop.
With 5 participants × 4000 characters that is 10 pairs × 16M cells ≈ **160M operations per
round** — on the strategy's own thread, between two LLM calls. It runs whenever
`StrategySelector` is configured (F-09 adaptive switching).

**Fix (done, and the duplication removed too).** `LevenshteinDistance` and `TextSimilarity`
existed as **two independent private copies** — one in `CouncilExecutor`, one in
`IStrategySelector` — with nothing keeping them in step. Both now call a single
`internal static Debate.TextSimilarity` with:

- **bounded input**: at most `MaxComparedLength` (1024) characters per text, so a pair costs
  a constant 2 × 1024² cell updates instead of scaling with the response;
- **a length-based short circuit**: the edit distance is at least `|lenA - lenB|`, so pairs
  that cannot reach the 0.8 stalemate threshold skip the character loop entirely;
- **stack allocation** for short rows, as before.

`AreNearIdentical` is the shared entry point for the stalemate check, so the threshold keeps
one meaning. The trade is documented: the heuristics look at the head of a response, not its
whole length.

**Acceptance.**
- [x] No O(len²) loop over full response bodies remains
- [x] 9 tests in `TextSimilarityTests`, including a 20 000-character pair that must finish
      under 250 ms and a case proving a difference inside the compared window is still seen
- [x] The duplicated implementations are gone from both call sites

---

## W2-05 · Log messages are built before the level check · **P1** · ⬜ todo

**Problem.** The round callback logs unconditionally:

```csharp
foreach (var (member, response) in round.Responses)
   Log(ExecutionLog.Trace("Participant", $"{member} responded ({response.Length} chars)"));
```

The interpolated string and the `ExecutionLog` record are allocated on every call; only
later does `ExecutionLogSink.Emit` consult `IsEnabled` (`DebateExecutionOptions.cs:75`).
With Trace disabled this is pure garbage, sized by `participants × rounds`.

**Fix.** Guard the Trace-level emissions with `IsEnabled` (or expose a
`LogIfEnabled(level, …)` helper on the executor) so nothing is allocated when the sink
is off.

> **Re-scoped after reading the code (2026-10-02).** The original framing was wrong: the
> interpolated string is *not* wasted, because `CouncilExecutor.Log` appends every entry to
> the public `ExecutionLogs` collection regardless of the logger, and `ExecutionLogSink.Emit`
> only gates the forwarding to `ILogger`. There is no level check being bypassed. What is
> actually missing is a knob: nothing lets a host say "do not collect Trace", so
> `_executionLogs` grows for the whole debate regardless. The task is therefore re-scoped to
> adding a minimum-level option to `DebateExecutionOptions` and guarding the Trace call sites
> with it — an additive, defaulted-to-unchanged change. Kept as ⬜ deliberately rather than
> half-done.

**Acceptance.**
- [ ] A minimum level can be configured, and Trace-level entries are neither interpolated
      nor collected below it
- [ ] `ExecutionLogs` behaviour unchanged when no level is configured

---

## W2-06 · Cache key hashes the whole knowledge base twice · **P1** · ✅ done

**Problem.** `DebateCacheKeyGenerator.Generate` (`:41-55`) serialises an anonymous object
whose `KnowledgeHash` is `SHA256(UTF8.GetBytes(context.KnowledgeContent))`, then hashes
the resulting JSON again. Per cached debate that is: a full UTF-8 copy of the knowledge
base, one hash over it, the JSON buffer, and a second hash.

**Fix.** Hash the knowledge content once and cache that digest on the `PromptContext`
record (its content is immutable), or switch to an incremental hash over the same buffer
that is being serialised.

**Fix (done), and it turned out to hide a correctness bug.** Two separate changes:

1. **The key was generated twice per debate** — once for the cache read and again for the
   write, each time allocating the member projection and re-encoding and re-hashing the whole
   knowledge base. It is now computed once before the try block and reused; the inputs it
   depends on are fixed for the duration of a debate.
2. **The key did not describe the debate.** `CouncilMember.DisplayName` is already
   `{model} ({provider})`, so model identity *was* covered — an earlier note in this plan
   claimed otherwise, and that was wrong. What was missing: the **chairman** (it produced the
   final verdict) and each member's **role and persona** (they change the prompt). Two
   debates with identical members but different chairmen shared a cache entry, so one caller
   received the other's verdict.

`DebateCacheKeyGenerator.KeyVersion` is now part of the hashed payload, so keys minted before
this change can never be read back as if they described the same debate.

**Acceptance.**
- [x] Knowledge content is encoded and hashed at most once per debate
- [x] 4 new tests: different chairmen differ, same chairman is stable, no-champion differs
      from a named one, and the key format is versioned
- [x] Documented consequence: existing cache entries are invalidated (changelog entry pending,
      W5-06)

---

## W2-07 · `YandexGptProvider`: throwaway request message and double payload copy · **P2** · ⬜ todo

**Problem.** `BuildNewAuthHeaders()` constructs an `HttpRequestMessage` purely to obtain a
header collection, while `:268` only uses those headers when `!UseNewEndpoint` — so on the
new endpoint a request message is allocated and discarded per call. Separately the payload
is `Serialize(...)` → `string` → `new StringContent(json, UTF8)`, i.e. the body is encoded
twice and held twice.

**Fix.** Build the headers only inside the branch that uses them; serialise with
`JsonSerializer.SerializeToUtf8Bytes` + `ByteArrayContent`.

**Acceptance.**
- [ ] No `HttpRequestMessage` allocated outside the branch that consumes it
- [ ] Existing provider tests stay green

---

## W2-08 · Operator regex scans every response even with no operator · **P2** · ✅ done

**Problem.** `DebateScenario.cs:262` runs `OperatorRequestRegex.Matches(response)` — a
compiled, `Singleline`, lazy `(.+?)` pattern — over every participant response on every
round, regardless of whether an Operator is attached.

**Fix (done).** A `response.Contains("[[", StringComparison.Ordinal)` pre-check skips the
regex entirely for responses that never delegate. The marker is a literal in the pattern, so
this cannot change which responses match.

**Acceptance.**
- [x] Regex is not entered for responses without the operator marker
- [x] Detection behaviour unchanged for responses that do use the marker (covered by the
      existing operator tests)

---

## W2-09 · Participant fan-out ignores the configured parallelism cap · **P2** · ✅ done

**Problem.** `DebateScenario.cs:58-71` fans out with a bare `Task.WhenAll`, while
`:277` and the voting path in `CouncilExecutor:1241` do honour
`ExecutionOptions.MaxDegreeOfParallelism`. On a large council the unbounded fan-out can
exhaust the `HttpClient` socket pool and multiply provider rate-limit errors.

**Fix.** Use the same `Parallel.ForEachAsync(..., ToParallelOptions(ct))` helper the rest
of the file already uses.

**Fix (done).** `CollectResponsesAsync` now takes the execution options and gates the
fan-out with a `SemaphoreSlim`. A semaphore rather than `Parallel.ForEachAsync` because the
results must keep member order: the disambiguation pass appends `"#2"`, `"#3"` in the order
results arrive, and `Parallel.ForEachAsync` would make those suffixes nondeterministic.

**Acceptance.**
- [x] The cap is respected in every fan-out path (participants, operator, knowledge keeper)
- [x] 5 tests in `DebateParallelismTests` with a probe provider that records peak concurrency
- [x] Regression guard verified by reverting the four files: with a cap of 2 and 5 members the
      old code peaked at **5**; with a cap of 1 it peaked at **5**. All five members still
      answer, and response order is unchanged
