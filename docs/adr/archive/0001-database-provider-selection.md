# ADR-0001: Support Selectable Database Provider (SQLite or SQL Server)

**Status:** Rejected (2026-09-11). Archived.
**Date:** 2026-08-30
**Deciders:** Project maintainer(s)

> **Outcome:** Not pursued. Travel Tracker stays SQL Server only. Kept for the record of options considered.

## Context

Travel Tracker is an open-source travel and expense tracker for businesses,
built on ASP.NET Core (net10.0) with EF Core 10 and ASP.NET Identity. It
currently targets **Microsoft SQL Server exclusively**. The project started
with SQLite in mind but moved to SQL Server during development.

We want to let whoever deploys the app choose the database at deployment time,
primarily SQLite or SQL Server. Two forces drive this:

1. **Cost / operational simplicity.** SQL Server carries licensing cost and
   requires a server to provision, back up, and maintain. SQLite is a single
   file with no server.
2. **Adoption.** As an open-source project, a "clone and run, no SQL Server
   required" path meaningfully lowers the barrier for others to try, self-host,
   and contribute.

**Workload profile (important constraint).** Expected usage is small: 10-15
users to start, up to ~100 over time, **never concurrent** and never heavy
simultaneous writes. The database is not, and will not be, a performance
bottleneck at this scale. This makes SQLite technically sufficient on the
merits, so the decision is about maintenance cost and correctness, not
throughput.

### What the current codebase makes easy or hard

Findings from the current `src/TravelTracker.Web` code:

- **Data access is EF Core with no raw SQL.** No `FromSql` / `ExecuteSql` /
  hand-written T-SQL anywhere. The query layer is provider-agnostic today.
  This is the single biggest factor in favor of feasibility.
- **Provider is wired in one place** (`UseSqlServer` in `Program.cs` and
  `Data/AppDbContextFactory.cs`). Switching is a small, localized change.
- **Concurrency uses Identity's string `ConcurrencyStamp`**, not a SQL Server
  `rowversion`/`timestamp`. That is portable to SQLite as-is.
- **Migrations are provider-specific.** Column types are baked into the
  migration files (`nvarchar(max)`, `datetimeoffset`, `decimal(18,2)`,
  `decimal(18,6)`, `date`, `bit`, `int`). A migration generated for SQL Server
  will not apply cleanly to SQLite.
- **Money and dates are the correctness risk.** Expenses and mileage use
  `decimal(18,2)` / `decimal(18,6)`; several columns use `datetimeoffset`.
  SQLite has no native decimal or datetime type (it stores them as TEXT/REAL),
  so server-side `SUM`/`ORDER BY`/range filters can lose precision or sort
  incorrectly without explicit handling.

## Decision

Introduce a **configuration-selected database provider**. The provider is read
from configuration (e.g. `Database:Provider` = `Sqlite` | `SqlServer`) and
selected at startup in both `Program.cs` and `AppDbContextFactory`. Maintain a
**separate migrations set per provider**. SQL Server remains the default so
existing deployments are unaffected; SQLite becomes a fully supported option.

## Options Considered

### Option A: SQL Server only (status quo)

| Dimension | Assessment |
|-----------|------------|
| Complexity | Low (nothing changes) |
| Cost | High for adopters (licensing + server ops) |
| Scalability | High (far beyond this workload) |
| Team familiarity | High |

**Pros:** No new work. One migration set, one test path. Strong decimal/date
semantics for money and reporting.
**Cons:** Highest barrier to adoption and self-hosting. Deployers pay for
capacity the workload will never use.

### Option B: SQLite only

| Dimension | Assessment |
|-----------|------------|
| Complexity | Low-Med (one-time migration to SQLite) |
| Cost | Zero |
| Scalability | Low, but sufficient for this workload |
| Team familiarity | Medium |

**Pros:** Simplest possible deploy and backup (one file). Zero cost. Great for
the stated scale.
**Cons:** Abandons existing SQL Server deployments and the option to grow into
concurrent-write scenarios. Decimal/date gotchas still apply and now have no
SQL Server fallback. Weak for future multi-writer or larger installs.

### Option C: Selectable provider, SQL Server default (CHOSEN)

| Dimension | Assessment |
|-----------|------------|
| Complexity | Medium (dual migrations + dual test path, ongoing) |
| Cost | Deployer chooses their cost |
| Scalability | High or low, deployer's choice |
| Team familiarity | Medium |

**Pros:** Deployers pick the trade-off that fits them. Best for open-source
adoption. Preserves existing SQL Server installs. SQLite path is ideal for
local dev, demos, and small self-hosted instances.
**Cons:** Two migration sets to generate and keep in sync. Test matrix doubles.
Decimal/date handling must be made explicitly provider-safe. Slightly more
config surface and documentation.

## Trade-off Analysis

The query layer is already portable (no raw SQL), so the hard part of
multi-provider EF support is mostly absent. The remaining cost concentrates in
three specific, well-understood places:

1. **Dual migrations.** EF migrations encode provider-specific column types, so
   one migration set cannot serve both providers. The accepted practice is a
   separate migrations assembly/folder per provider, selected at runtime via
   `MigrationsAssembly`. Cost: every schema change is generated and reviewed
   twice.

2. **Decimal and datetime correctness on SQLite.** This is the only real
   *correctness* risk, and it matters because this is an expense app where a
   wrong total is a real bug. Mitigation: value converters for money columns
   and care with `datetimeoffset`, backed by tests that run on both providers.

3. **Test matrix.** Behavior must be verified on both providers so they do not
   drift. The existing `test/TravelTracker.Tests` project is the natural home;
   SQLite (in-memory or file) makes a fast second test lane cheap to run.

Given the workload (small, non-concurrent), SQLite is not a performance
compromise. The decision reduces to: is the recurring dual-migration and
dual-test tax worth the adoption and cost benefits? For an open-source project
meant for others to self-host, yes.

## Consequences

**Easier**
- Adopters can run the app with zero database cost and no server to manage.
- Local development, CI, and demos can use a throwaway SQLite file.
- Existing SQL Server deployments keep working unchanged (default preserved).

**Harder**
- Every schema change requires generating and reviewing two migrations.
- CI and local testing must cover both providers to prevent drift.
- Money/date handling must stay provider-safe; a naive change could pass on
  SQL Server and silently misbehave on SQLite.

**To revisit**
- If a future feature needs SQL-Server-only capabilities (stored procs,
  advanced T-SQL, heavy concurrent writes), reassess whether SQLite stays a
  first-class target or drops to "dev/demo only."
- If dual-migration maintenance proves too costly relative to actual SQLite
  adoption, consider demoting SQLite to a dev-only convenience.

## Action Items

1. [ ] Add `Microsoft.EntityFrameworkCore.Sqlite` package reference.
2. [ ] Read provider from config (`Database:Provider`, default `SqlServer`) and
       branch `UseSqlServer` / `UseSqlite` in `Program.cs` and
       `AppDbContextFactory` (`MigrationsAssembly` per provider).
3. [ ] Create a separate SQLite migrations set (own output dir/assembly);
       keep the existing SQL Server migrations as the default set.
4. [ ] Add decimal value converters (and audit `datetimeoffset` usage) so
       money aggregation and date ordering are correct on SQLite.
5. [ ] Add a SQLite test lane in `test/TravelTracker.Tests` alongside the
       SQL Server path; run both in CI.
6. [ ] Document the `Database:Provider` setting and per-provider connection
       string / setup in the README.
7. [ ] Verify `db.Database.MigrateAsync()` on startup applies the correct
       provider's migrations for a fresh SQLite file and an existing SQL
       Server database.
