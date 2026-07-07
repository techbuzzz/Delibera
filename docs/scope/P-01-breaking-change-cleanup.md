# P-01 · Breaking-Change Cleanup

> **Version:** 10.3.0  
> **Status:** 🔵 In Scope  
> **Effort:** S — 1–2 days  
> **Branch:** `feature/p-01-breaking-change-cleanup`  
> **Depends on:** —  
> **Blocks:** P-02 (Delibera.Server), P-03 (Delibera.Grpc), P-04 (NuGet GA)

---

## Goal

Remove all backward-compatibility shims, deprecated members, and naming inconsistencies accumulated since v10.1.  
The result is a **clean, minimal public API surface** that becomes the stable foundation for the v10.3.0 NuGet GA release.

---

## Scope

### BC-01 · Remove `[Obsolete]` members

All members marked `[Obsolete]` are removed. Callers must migrate before upgrading to v10.3.0.

| Member | Location | Replacement |
|--------|----------|-------------|
| `static class Moderator` (alias for `Chairman`) | `src/Delibera.Core/Council/Chairman.cs` | Use `Chairman` directly |
| `ICouncilBuilder.SetModerator(CouncilMember)` | `src/Delibera.Core/Council/CouncilBuilder.cs` | `SetChairman(CouncilMember)` |
| `ICouncilBuilder.SetModerator(string, ILLMProvider, string?)` | `src/Delibera.Core/Council/CouncilBuilder.cs` | `SetChairman(string, ILLMProvider, string?)` |

**Files to touch:**
- `src/Delibera.Core/Council/Chairman.cs` — delete `Moderator` class
- `src/Delibera.Core/Council/CouncilBuilder.cs` — delete both `SetModerator` overloads
- `src/Delibera.Core/Interfaces/ICouncilBuilder.cs` — remove `SetModerator` declarations (if present)

---

### BC-02 · Remove `IDebateStrategyWithOptions` shim

`IDebateStrategyWithOptions` was introduced to add `DebateExecutionOptions` (response language, parallelism, logger) to `ExecuteAsync` without breaking external `IDebateStrategy` implementations.  
Now that the shim has served its purpose, merge the signature directly into `IDebateStrategy` and remove the intermediate interface.

**Changes:**

1. **`IDebateStrategy.cs`** — merge `IDebateStrategyWithOptions.ExecuteAsync(...)` signature as the canonical overload on `IDebateStrategy`. Remove the default-method fallback on the old overload (or delete the old overload entirely).

2. **`IDebateStrategyWithOptions.cs`** — delete the file.

3. **`DebateScenario.cs`** — change `public abstract class DebateScenario : IDebateStrategyWithOptions` → `public abstract class DebateScenario : IDebateStrategy`. Update `ExecuteAsync` signature accordingly.

4. **All concrete strategies** (`StandardDebateStrategy`, `CritiqueDebateStrategy`, `ConsensusDebateStrategy`, `AdaptiveStrategySelector`) — update `ExecuteAsync` signatures, remove `IDebateStrategyWithOptions` references.

5. **`CouncilExecutor.cs`** — remove the `is IDebateStrategyWithOptions` cast / pattern match; call the unified `ExecuteAsync` directly.

**Migration note for external strategy implementors:**
```csharp
// BEFORE (v10.2.x) — implement either interface:
public class MyStrategy : IDebateStrategyWithOptions { ... }
// OR with default method fallback:
public class MyStrategy : IDebateStrategy { ... }

// AFTER (v10.3.0) — implement IDebateStrategy with the full signature:
public class MyStrategy : IDebateStrategy
{
    public Task<DebateResult> ExecuteAsync(
        IReadOnlyList<CouncilMember> members,
        PromptContext context,
        CouncilMember? chairman,
        KnowledgeKeeper? knowledgeKeeper,
        Operator? op,
        DebateExecutionOptions options,
        int maxRounds,
        float temperature,
        Action<DebateRound>? onRoundCompleted,
        CancellationToken ct) { ... }
}
```

---

### BC-03 · `ILLMProvider.GetModelCapabilitiesAsync` — remove default `null` return

Currently `GetModelCapabilitiesAsync` has a default interface method implementation returning `null`, allowing providers to opt out. This makes callers write null-guard branches everywhere.

**Changes:**

1. **`ILLMProvider.cs`** — remove the default `null` implementation. The method becomes abstract (no default body).

2. **All `ILLMProvider` implementations** (`OllamaProvider`, `ChatClientLLMProvider`, any other) — implement `GetModelCapabilitiesAsync` explicitly. Providers that genuinely cannot introspect capabilities return `ModelCapabilities.Unknown` (a new sentinel value) instead of `null`.

3. **`ModelCapabilities`** — add `Unknown` static property / sentinel instance so callers can distinguish "not supported" from null.

4. **`CouncilExecutor` / `ModelContextWindowRegistry`** — replace `?? fallback` null-guard patterns with `== ModelCapabilities.Unknown` checks.

**Migration note:**
```csharp
// BEFORE:
var caps = await provider.GetModelCapabilitiesAsync(model, ct);
if (caps is null) { /* fallback */ }

// AFTER:
var caps = await provider.GetModelCapabilitiesAsync(model, ct);
if (caps == ModelCapabilities.Unknown) { /* fallback */ }
```

---

### BC-04 · Rename `RagProviderFactory` → `VectorStoreFactory`

`RagProviderFactory` conflates two concepts: it is a factory for vector-store providers (Qdrant, pgvector), not for "RAG" as a whole (which also includes embedding, chunking, compression). The rename makes the responsibility explicit.

**Files to rename / update:**

| Old name | New name |
|----------|----------|
| `src/Delibera.Core/Providers/RAG/RagProviderFactory.cs` | `VectorStoreFactory.cs` |
| `class RagProviderFactory` | `class VectorStoreFactory` |
| `interface IRagProviderFactory` (`src/Delibera.Core/Interfaces/IRagProviderFactory.cs`) | `interface IVectorStoreFactory` + file rename |

**Call sites to update:**

| File | Change |
|------|--------|
| `src/Delibera.Core/DependencyInjection/ServiceCollectionExtensions.cs` | `services.TryAddSingleton<IVectorStoreFactory, VectorStoreFactory>()` |
| `src/Delibera.ConsoleApp/Program.cs` (×2) | `new VectorStoreFactory()` |
| `src/Delibera.ConsoleApp/Examples/RagExample.cs` | `new VectorStoreFactory()` |
| `src/Delibera.Core/Providers/ProviderFactory.cs` (XML doc) | Update `cref` |
| `README.md`, `README-RU.md`, `src/README.md`, `src/Delibera.Core/README.md` | Update table + code samples |
| `docs/QuickStart.md`, `docs/QuickStart-RU.md` | Update code samples |

**Transition approach:** Add a `[Obsolete]` type alias in `RagProviderFactory.cs` for one minor version if desired — but since this is itself a breaking-change release, a direct rename is acceptable. Document in CHANGELOG with a clear migration note.

---

### BC-05 · Consolidate `CouncilBuilder` method overloads (API surface reduction)

Audit all overloads on `CouncilBuilder` / `ICouncilBuilder` and collapse those that differ only by optional parameters into a single method with `= default` arguments.

**Candidates to consolidate (verify during implementation):**

| Overload group | Action |
|---------------|--------|
| `AddMember(string, ILLMProvider, string, MemberCapabilities, string?)` and `AddMember(string, ILLMProvider, string?, string?)` | Keep only the `MemberCapabilities`-bearing overload; set default `MemberCapabilities = MemberCapabilities.Auto` |
| `WithChairman(CouncilMember)` vs `SetChairman(CouncilMember)` | Standardise on `SetChairman`; check if `WithChairman` is used externally |
| `WithTelemetry(TelemetryOptions?)` vs `WithTelemetry(Action<TelemetryOptions>)` | Both are valid and symmetric — keep both, no action needed |

**Rule of thumb:** if two overloads can be expressed as one method with `= default` without ambiguity, collapse them. If the signatures are too different, keep both and document the preferred one.

---

## Out of Scope

The following items are **explicitly excluded** from P-01 and belong to later milestones:

- Adding new public API members (→ P-02, P-03, I-01…)
- Changing `DebateExecutionOptions` fields (→ only signature unification is in scope)
- Updating test fixtures beyond what is broken by the above changes
- Performance tuning
- Documentation rewrite (only inline migration notes + CHANGELOG)

---

## Acceptance Criteria

- [ ] `[Obsolete]` attributes removed — `dotnet build` produces **zero** CS0618/CS0619 warnings
- [ ] `IDebateStrategyWithOptions` interface file deleted — no references remain in `Delibera.Core`
- [ ] `ILLMProvider.GetModelCapabilitiesAsync` has no default implementation — all providers implement it explicitly
- [ ] `RagProviderFactory` / `IRagProviderFactory` renamed to `VectorStoreFactory` / `IVectorStoreFactory` — zero references to old names in non-doc code
- [ ] All overload consolidations complete — `ICouncilBuilder` method count ≤ baseline minus consolidated count
- [ ] **All existing tests pass** (`dotnet test`) — no new test failures introduced
- [ ] `CHANGELOG.md` entry added under `[10.3.0]` with a **Breaking Changes** section listing every removed member and its replacement
- [ ] `README.md` and `README-RU.md` code samples reflect new names

---

## Migration Guide (CHANGELOG excerpt)

```
### ⚠️ Breaking Changes in v10.3.0 (P-01)

| Removed | Replacement |
|---------|-------------|
| `Moderator` static class | `Chairman` |
| `ICouncilBuilder.SetModerator(...)` | `ICouncilBuilder.SetChairman(...)` |
| `IDebateStrategyWithOptions` interface | `IDebateStrategy` (full signature) |
| `GetModelCapabilitiesAsync` default null return | Implement explicitly; return `ModelCapabilities.Unknown` |
| `RagProviderFactory` / `IRagProviderFactory` | `VectorStoreFactory` / `IVectorStoreFactory` |
```

---

## Branching & PR

```bash
git checkout develop
git pull
git checkout -b feature/p-01-breaking-change-cleanup
# ... make changes ...
git push -u origin feature/p-01-breaking-change-cleanup
# Open PR → develop
```

PR title: `feat(core): P-01 breaking-change cleanup for v10.3.0`  
Reviewer checklist: run `dotnet build`, `dotnet test`, grep for old names.

---

*Delibera · P-01 Scope · July 2026*
