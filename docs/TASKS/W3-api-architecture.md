# W3 — API & Architecture

> **Goal:** make the public API tell the truth, and stop `CouncilExecutor` from growing.
> **Policy (owner, 2026-10-02):** safe changes ship in 10.3.1; anything that alters a
> public signature is written up here and lands in 10.4.0 with a migration note.
> **Status:** 3 / 8 done (W3-01, W3-02, W3-03)

---

## W3-01 · Remove the dead `WeightedVotingStrategy(defaultWeight)` parameter · **P1** · ✅ done

**Problem.** `Voting/IVotingStrategy.cs:143`:

```csharp
public sealed class WeightedVotingStrategy(double defaultWeight = 1.0) : IVotingStrategy
```

`defaultWeight` is never read (compiler: `CS9113: Parameter 'defaultWeight' is unread`).
`ResolveWeight` (`:181-186`) returns the explicit `MemberWeights` entry, else
`ballot.Weight`. The class documentation describes only those two rules and never mentions
`defaultWeight`.

So `new WeightedVotingStrategy(2.0)` compiles, reads as "double the vote", and changes
nothing. A caller who relies on it gets silently different results than the API promises.

**Fix (owner decision 2026-10-02: remove the parameter).** Drop the primary-constructor
argument; the class keeps `MemberWeights` + per-ballot weights, exactly as documented.
Record it under Breaking Changes in the CHANGELOG with the note that the argument was
never functional.

**Acceptance.**
- [x] CS9113 gone
- [x] Voting tests green (438 total, unchanged behaviour)
- [ ] CHANGELOG entry: `WeightedVotingStrategy(double)` → `WeightedVotingStrategy()` (W5-06)

---

## W3-02 · Remove the dead `CouncilBuilder._persistedOptionsSnapshot` field · **P2** · ✅ done

**Problem.** `Council/CouncilBuilder.cs:45` declares `private CouncilOptions?
_persistedOptionsSnapshot;`. Grep over the whole repository finds exactly one occurrence —
the declaration itself (`CS0169: field is never used`). It is a leftover of a persistence
feature that is implemented elsewhere (`CouncilOptionsSnapshot()` in `CouncilExecutor`).

**Fix.** Delete the field.

**Acceptance.**
- [x] CS0169 gone
- [x] `grep -r "_persistedOptionsSnapshot"` returns nothing (1 occurrence before, the declaration)

---

## W3-03 · `MemoryEntry.Metadata` nullability · **P1** · ✅ done

**Problem.** `Memory/IAgentMemory.cs` — build reports:
```
CS8604: Possible null reference argument for parameter 'Metadata'  (×2, lines 215, 294)
CS8602: Dereference of a possibly null reference                  (line 292)
```
`MemoryEntry`'s constructor takes a non-nullable `IReadOnlyDictionary<string, string>`, but
three in-memory implementations can hand it a null dictionary — so every consumer must
re-guard, and an exception can escape from a non-throwing-looking path.

**Fix.** Normalise at the seam: `Metadata ?? EmptyMetadata` in the implementations, and
document the parameter as never null.

**Fix (done, and it was more than a warning).** `VectorSearchResult.Metadata` is nullable.
`PgVectorAgentMemory.RecallAsync` dereferenced it directly in a `.Where(...)` predicate, so a
vector-store hit stored without tags would have thrown a `NullReferenceException` and taken
recall down. Both recall sites now normalise with `r.Metadata ?? MemoryEntry.Empty`, the
predicate checks for null first, and the record documents the non-null contract. The public
`MemoryEntry` signature was deliberately left non-nullable — making the property nullable would
push the null check onto every consumer instead of fixing it once at the seam.

**Acceptance.**
- [x] CS8602 and both CS8604 gone
- [x] `MemoryEntry.Empty` is the single shared empty dictionary
- [ ] Test with a search result whose `Metadata` is null (needs a store fake — the RAG test
      double from W2-02 can be extended)

---

## W3-04 · `CouncilOptionsSnapshot` silently drops 5 settings · **P1** · ⬜ todo

**Problem.** `CouncilExecutor.CouncilOptionsSnapshot()` (`:1156-1167`) persists Strategy,
MaxRounds, Temperature, SystemPrompt, ResponseLanguage and MaxDegreeOfParallelism — and
nothing else. Compression, AutoChunking, telemetry, cache behaviour and persistence
options are absent, while the surrounding documentation promises that a resumed debate
"reapplies the same settings".

**Consequence.** A resumed debate runs with different compression/chunking behaviour than
the one it continues, and the checkpoint does not record that it did so.

**Fix.** Either persist the remaining sections in `CouncilOptions`, or correct the
documentation to state precisely which settings are restored. Persisting is preferable;
correcting the doc is the safe 10.3.1 half.

**Acceptance.**
- [ ] Doc and code agree
- [ ] Test: round-trip of a snapshot restores every field it claims to

---

## W3-05 · `CouncilBuilder` documentation contradicts its code · **P2** · ⬜ todo

**Problem.** `CouncilBuilder.cs:648-649` states that "explicit builder calls take precedence"
over `WithOptions`. `ApplyOptions` (`:646-702`) applies values by a "non-default"
heuristic, so `WithOptions` **overwrites** fields the caller already set explicitly. The
documented and actual precedence are opposite.

**Fix (safe half).** Correct the XML doc to state the real rule ("`WithOptions` is applied
last and wins"). The stricter fix — tracking which values were set explicitly — is a
behaviour change and goes to 10.4.0.

**Acceptance.**
- [ ] Doc matches behaviour
- [ ] Test documenting the actual precedence

---

## W3-06 · Decompose `CouncilExecutor` · **P1** · ⬜ todo

**Problem.** `CouncilExecutor` is 1332 lines with a 27-argument internal constructor
(`:39-66`) and eight unrelated responsibilities: debate lifecycle, result caching, telemetry,
attachments, agent memory, auto-chunking, checkpointing, voting, adaptive switching, text
similarity, and a 89-line `GetInfo()` formatter. Every new feature since v10.2.0 has been
added to this one class, which is the mechanical reason the pieces "don't fit together".

**Target shape (no public API change — properties keep their names, bodies become
`=> _config.X`):**

| New internal type | Extracts |
|---|---|
| `DebateExecutionConfig` (record) | the 27 constructor parameters, grouped into composition / compression / observability / persistence / output / memory / caching |
| `DebateCacheCoordinator` | cache get + stamp (`:300-315`, `:336-346`) — also removes the duplicated `DebateCacheKeyGenerator.Generate` call |
| `DebateMemoryCoordinator` | recall + store (`:711-747`, `:948-1000`) — collapses three identical `catch → ReportError("AgentMemory")` blocks |
| `AttachmentContextAugmenter` | `:749-788` |
| `RoundObserver` | the round callback `:828-882` (logging, telemetry, adaptive switch, checkpoint) |
| `TextSimilarity` (static) | `:1076-1103` + `:1169-1204` |
| `CouncilOptionsSnapshotFactory` | `:1156-1167` |

**Acceptance.**
- [ ] `CouncilExecutor` ≤ ~600 lines, constructor ≤ ~8 parameters
- [ ] No public member removed or renamed
- [ ] All 403 tests green after each extraction step (one type per commit)

---

## W3-07 · Async round callback · **P1** · 🔒 10.4.0

**Problem.** `IDebateStrategy.ExecuteAsync` accepts `Action<DebateRound>? onRoundCompleted`.
A synchronous callback forces the executor to block on asynchronous work
(`CouncilExecutor:860-863`, `:876`): one thread-pool thread is blocked per round for the
duration of a file/network checkpoint write, and `OperationCanceledException` is
repackaged as `AggregateException` (see W1-06). This is the structural cause of two
separate defects.

**Fix (10.4.0).** `Func<DebateRound, CancellationToken, ValueTask>? onRoundCompleted` on
`IDebateStrategy`, awaited by the strategies. Built-in strategies and the public
`OnRoundCompleted` event keep working — the event stays a fire-and-forget `Action`, the
strategy API becomes awaitable.

**Acceptance.**
- [ ] No `GetAwaiter().GetResult()` in `CouncilExecutor`
- [ ] Cancellation propagates as `OperationCanceledException`
- [ ] Migration note in CHANGELOG with before/after code

---

## W3-08 · Overlapping abstractions · **P2** · 🔒 10.4.0

**Problem.**
- `TokenCounter.EstimateTokens(null)` does not compile: the call is ambiguous between
  `EstimateTokens(string?)` and `EstimateTokens(IEnumerable<string>)`. Found while writing
  the W2-01 tests. A caller has to write `(string?)null`, which is a small but real papercut
  in a public API.
- `IRagProvider.SearchAsync` (`Interfaces/IRagProvider.cs:55`) and
  `IVectorStore.SearchAsync` (`Interfaces/IVectorStore.cs:61`) have the same semantics;
  `BaseRagProvider` holds a `VectorStore` and simply forwards — so the "RAG provider" layer
  adds a concept without adding behaviour, and W2-02's double search was a symptom.
- `IDebateStore` (persistence) and `IDebateCache` (caching) are two independent
  "JSON on disk" implementations with duplicated TTL/keying logic.
- `IDebateOrchestrator` mixes `ValueTask` (`GetStatusAsync`) and `Task` for the rest;
  `LocalDebateOrchestrator` returns `Task.FromResult` where `ValueTask` avoids the
  allocation.

**Fix (10.4.0).** Collapse to one storage abstraction underneath both; make
`IRagProvider` a thin adapter over `IVectorStore`; settle on one task type per interface.

**Acceptance.**
- [ ] Documented in CHANGELOG as a consolidation, with deprecation shims where needed
