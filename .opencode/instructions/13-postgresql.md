# PostgreSQL Development Guidelines

## Scope
Applies to all database-related code, migrations, and SQL queries.

---

## Core Rules

- Always use **parameterized queries** — never string interpolation in SQL.
- Use `EXPLAIN (ANALYZE, BUFFERS)` to investigate slow queries before optimizing.
- Use **connection pooling** (pgBouncer or built-in EF Core pooling) for high-concurrency scenarios.
- Run `VACUUM ANALYZE` regularly on high-write tables.
- Use `NOW()` / `CURRENT_TIMESTAMP` for server-side timestamps.
- Prefer `TIMESTAMPTZ` over `TIMESTAMP` for all datetime columns.
- Use `UUID` (or `gen_random_uuid()`) for primary keys.

---

## Indexing Strategy

- Create indexes for all foreign keys.
- Create indexes for columns used in `WHERE`, `ORDER BY`, `JOIN` conditions on large tables.
- Use **partial indexes** for filtered queries (e.g., `WHERE status = 'Active'`).
- Use **composite indexes** for multi-column searches — order matters (most selective first).
- Use **GIN indexes** for JSONB and full-text search columns.
- Monitor and remove unused indexes — they slow down writes.

```sql
-- Partial index example
CREATE INDEX idx_invoices_active ON invoices (user_id, created_at)
WHERE status = 'Active';

-- GIN index for JSONB
CREATE INDEX idx_events_data_gin ON events USING gin(data);

-- Composite index
CREATE INDEX idx_invoices_user_status ON invoices (user_id, status, created_at DESC);
```

---

## JSONB Operations

```sql
-- Containment query (uses GIN index)
SELECT * FROM events WHERE data @> '{"type": "login"}';

-- Path query
SELECT data #>> '{user,role}' FROM events WHERE data ? 'user_id';

-- JSONB aggregation
SELECT jsonb_agg(data) FROM events WHERE data ? 'user_id';

-- Update nested field
UPDATE events SET data = jsonb_set(data, '{status}', '"processed"') WHERE id = $1;
```

---

## Window Functions

```sql
-- Row number per user
SELECT
    id,
    user_id,
    created_at,
    ROW_NUMBER() OVER (PARTITION BY user_id ORDER BY created_at DESC) AS rn
FROM invoices;

-- Running total
SELECT
    id,
    amount,
    SUM(amount) OVER (PARTITION BY user_id ORDER BY created_at) AS running_total
FROM invoices;
```

---

## Full-Text Search

```sql
-- Create tsvector column
ALTER TABLE invoices ADD COLUMN search_vector tsvector
    GENERATED ALWAYS AS (to_tsvector('russian', coalesce(notes, '') || ' ' || coalesce(client_name, ''))) STORED;

CREATE INDEX idx_invoices_fts ON invoices USING gin(search_vector);

-- Search
SELECT * FROM invoices WHERE search_vector @@ plainto_tsquery('russian', 'дизайн сайт');
```

---

## Pagination

```sql
-- Keyset pagination (preferred for large datasets)
SELECT * FROM invoices
WHERE user_id = $1
  AND (created_at, id) < ($2, $3)
ORDER BY created_at DESC, id DESC
LIMIT 20;

-- Offset pagination (only for small datasets)
SELECT * FROM invoices
WHERE user_id = $1
ORDER BY created_at DESC
LIMIT 20 OFFSET $2;
```

---

## EF Core + PostgreSQL Conventions

- Use `HasDefaultValueSql("NOW()")` for timestamp columns.
- Use `HasMaxLength` + `IsRequired` for all string columns.
- Use `HasConversion<string>()` for enum columns.
- Use `UseSnakeCaseNamingConvention()` (Npgsql) for consistent naming.
- Use `AsNoTracking()` for read-only queries.
- Avoid `Include()` chains deeper than 2 levels — use projections instead.

```csharp
// Projection instead of deep Include
var result = await context.Invoices
    .Where(i => i.UserId == userId)
    .Select(i => new InvoiceListItem(
        i.Id,
        i.Number,
        i.Status,
        i.TotalAmount,
        i.Client.Name,
        i.CreatedAt))
    .AsNoTracking()
    .ToListAsync(ct);
```

---

## Migrations

- Always review generated migrations before applying.
- Never edit applied migrations — create a new one.
- Use descriptive migration names: `AddInvoiceSearchVector`, `AddClientEmailIndex`.
- Test migrations on a copy of production data before deploying.
- Include `DOWN` migration logic for rollback capability.

---

## Optimization Checklist

- [ ] `EXPLAIN ANALYZE` run for queries on tables > 10k rows
- [ ] Foreign keys have indexes
- [ ] No sequential scans on large tables in production queries
- [ ] JSONB columns have GIN indexes if queried
- [ ] Pagination uses keyset for large datasets
- [ ] `AsNoTracking()` used for read-only EF queries
- [ ] Connection pooling configured
- [ ] `pg_stat_statements` enabled for query monitoring
