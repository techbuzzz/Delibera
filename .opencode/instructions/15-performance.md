# Performance Optimization Guidelines

## Scope
Applies to all files.

---

## Golden Rules

1. **Measure first** — profile before optimizing. Never guess.
2. **Optimize the bottleneck** — fix the slowest part, not the easiest.
3. **Correctness over speed** — a fast wrong answer is worse than a slow correct one.
4. **Document optimizations** — explain why non-obvious code exists.

---

## .NET / C# Performance

### Memory & Allocations
- Avoid unnecessary allocations on hot paths.
- Use `Span<T>` and `Memory<T>` for buffer operations instead of arrays.
- Use `ArrayPool<T>.Shared` for temporary large arrays.
- Use `StringBuilder` for string concatenation in loops.
- Prefer `struct` for small, short-lived value types (< 16 bytes).
- Use `record struct` for immutable value types.
- Avoid boxing — prefer generic methods over `object` parameters.

### Async & Concurrency
- Use `async`/`await` for all I/O-bound operations — never block threads.
- Use `ValueTask<T>` for frequently synchronous hot paths.
- Use `Task.WhenAll()` for parallel independent I/O operations.
- Use `IAsyncEnumerable<T>` for streaming large result sets.
- Use `Channel<T>` for producer-consumer patterns.
- Avoid `Task.Run()` for I/O — it wastes thread pool threads.

### Collections
- Use `List<T>` with initial capacity when size is known: `new List<T>(expectedCount)`.
- Use `Dictionary<TKey, TValue>` for O(1) lookups.
- Use `HashSet<T>` for membership checks.
- Use `ImmutableArray<T>` for read-only collections shared across threads.
- Avoid LINQ in tight loops — prefer `for`/`foreach`.

### EF Core
- Use `AsNoTracking()` for all read-only queries.
- Use projections (`Select`) instead of loading full entities when only a few fields are needed.
- Avoid N+1 queries — use `Include()` or split queries.
- Use `ExecuteUpdateAsync()` / `ExecuteDeleteAsync()` for bulk operations (EF Core 7+).
- Use compiled queries for frequently executed parameterized queries.

```csharp
// Compiled query
private static readonly Func<AppDbContext, Guid, Task<Invoice?>> GetInvoiceById =
    EF.CompileAsyncQuery((AppDbContext ctx, Guid id) =>
        ctx.Invoices.FirstOrDefault(i => i.Id == id));
```

---

## API Performance

- Use response compression (`AddResponseCompression`).
- Use output caching for stable, frequently-read endpoints.
- Use `IMemoryCache` or `IDistributedCache` (Redis) for expensive computations.
- Return `IAsyncEnumerable<T>` for large collections to enable streaming.
- Use `CancellationToken` everywhere — abort work when client disconnects.
- Paginate all list endpoints — never return unbounded collections.

---

## Database Performance

- Index foreign keys and frequently queried columns.
- Use keyset pagination for large datasets.
- Use `EXPLAIN ANALYZE` before deploying queries on large tables.
- Avoid `SELECT *` — project only needed columns.
- Use connection pooling.
- Batch inserts/updates where possible.

---

## Frontend / Blazor Performance

- Minimize component re-renders — use `@key` for list items.
- Use `ShouldRender()` to prevent unnecessary renders.
- Lazy-load heavy components with `<Suspense>` / dynamic imports.
- Minimize JS interop calls — batch when possible.
- Use `StateHasChanged()` only when necessary.

---

## Observability

- Add structured logging with timing for slow operations.
- Use `Activity` / `Stopwatch` to measure critical paths.
- Integrate with Application Insights or OpenTelemetry.
- Set up alerts for p95/p99 latency thresholds.
- Monitor memory pressure and GC metrics in production.

---

## Checklist

- [ ] No blocking calls in async context
- [ ] `AsNoTracking()` on read-only EF queries
- [ ] No N+1 queries
- [ ] List endpoints are paginated
- [ ] `CancellationToken` passed through all async calls
- [ ] No unbounded collections returned from API
- [ ] Caching applied where appropriate with invalidation strategy
- [ ] Performance-critical paths profiled with real data
