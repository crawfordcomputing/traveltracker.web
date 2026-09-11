# ADR-0004: Read-Only Trip Reference Code (Year + Random)

**Status:** Accepted (implemented 2026-09-11)
**Date:** 2026-09-11
**Deciders:** Project maintainer(s)

## Context

Travelers need a stable, human-friendly ID to reference a trip from other
systems. The immediate case is reimbursement in Concur: the traveler types a
trip reference into the Concur report so finance can match it back to the
approved trip here. Other uses (tickets, emails, spreadsheets) follow the same
pattern: a person reads the ID in this app and types or pastes it elsewhere.

Confirmed from the current code:

- `Trip` has only the database `Id` (int identity). It appears in URLs
  (`/Trips/Details/{id}`) and the ICS UID (`trip-{Id}@traveltracker.local`),
  but it is not presented as a reference and exposes the raw DB key.
- Trips are created in four places: `Trips/Create`,
  `Trips/Index.OnPostCloneAsync`, and the `NewTrip` helpers in `DevSeeder`
  and `EvalSeeder`. Code-assignment logic placed in one page would miss the
  others.
- `Trips/Edit` binds an `InputModel` and copies fields explicitly, so a new
  entity property is not overpostable through that page.
- `Trips/Index` already has a free-text `Search` (Purpose + destination city).
- Reports export only aggregates (by department, category, cost center,
  person). There is no per-trip CSV today.
- The app already uses `RandomNumberGenerator` for generated secrets
  (`InviteTokens`, `TempPassword`), so crypto-strong generation is an
  established pattern.

## Decision

Add an immutable, unique, human-readable **trip code** to every trip, made of
the creation year plus a random suffix.

### Format

`TT-{YYYY}-{XXX}-{XXX}`, for example `TT-2026-7K4-Q9M` (15 characters). The
six random characters are split 3+3 so the code is easier to read aloud (open
question 3).

- **Prefix** `TT`. Constant in code (`TripCode.Prefix`). Not configurable in
  v1, so every code in a database has one shape (see Open questions).
- **Year** = the UTC year of `Trip.CreatedAt`, **not** `StartDate`. The start
  date is editable; the code must never change after it is issued. A trip
  created in December 2026 for a January 2027 trip is `TT-2026-...`. The year
  is context for humans only; uniqueness does not depend on it.
- **Random part**: 6 characters from the Crockford Base32 alphabet
  `0123456789ABCDEFGHJKMNPQRSTVWXYZ`.
  - Leaves out `I`, `L`, `O` (easily confused with `1` and `0`) and `U`
    (reduces accidental words).
  - Each character is drawn with `RandomNumberGenerator.GetInt32(32)`.
  - 32^6 = about 1.07 billion possible codes per year.
- **Input is forgiving, storage is strict.** Lookups and search normalize
  input before matching: trim, upper-case, remove spaces and hyphens, then map
  `O -> 0` and `I`/`L -> 1` (the Crockford decoding rules). So
  `tt 2026 7k4q9m` and `TT-2026-7K4-Q9M` find the same trip. Codes are always
  stored and displayed in the canonical upper-case, hyphenated form.

### Uniqueness and collisions

- `Trip.Code`: `nvarchar(20)`, required, **unique index**.
- On generation, check `Trips.AnyAsync(t => t.Code == candidate)` and draw
  again on a hit (bounded, e.g. 5 attempts, then throw).
- The unique index is the backstop for the practically impossible race where
  two concurrent saves draw the same unused code. That save fails like any
  other DB error; retrying the action succeeds.
- Expected clash rates (a clash only costs one extra draw):

  | Trips per year | Chance a new code clashes with an existing one | Chance of at least one clash that year |
  |----------------|-----------------------------------------------|---------------------------------------|
  | 1,000 | 0.0001% | about 0.05% |
  | 10,000 | 0.001% | about 5% |
  | 100,000 | 0.01% | near certain (still just a redraw) |

- Optional: reject any draw that contains a word from a small blocklist before
  accepting it. Cheap, but not required for v1.

### Assignment

- Assigned centrally in `AppDbContext.SaveChangesAsync` (override or
  `SaveChangesInterceptor`): every `Added` `Trip` with an empty `Code` gets
  one. This covers Create, Clone, DevSeeder, and EvalSeeder with no caller
  changes.
- Pure logic lives in a static `Domain/TripCode.cs`:
  `Generate(year)`, `Normalize(input)`, `TryParse(input, out code)`, and
  `TryParseSuffix(input, out suffix)` for random-part-only search.
  Matches the "pure domain logic, pages load data" convention.
- **Clone never copies the code.** The clone is a new trip and gets a new code.
- Issued at creation (Draft), not at approval. Travelers often need the
  reference before approval (pre-trip requests, booking notes).
- No counter table, no locking, no raw SQL at runtime.

### Backfill

Existing trips get codes in the migration, in T-SQL, using the same alphabet.
`CRYPT_GEN_RANDOM` gives crypto-random bytes; `byte % 32` is unbiased because
256 divides evenly by 32.

```sql
DECLARE @a char(32) = '0123456789ABCDEFGHJKMNPQRSTVWXYZ';
DECLARE @id int, @y int, @code nvarchar(20), @rand nvarchar(6), @b varbinary(6), @i int;
DECLARE c CURSOR LOCAL FAST_FORWARD FOR
    SELECT Id, YEAR(CreatedAt) FROM Trips WHERE Code IS NULL;
OPEN c; FETCH NEXT FROM c INTO @id, @y;
WHILE @@FETCH_STATUS = 0
BEGIN
    WHILE 1 = 1
    BEGIN
        SET @b = CRYPT_GEN_RANDOM(6);
        SET @rand = N'';
        SET @i = 1;
        WHILE @i <= 6
        BEGIN
            SET @rand += SUBSTRING(@a, CAST(SUBSTRING(@b, @i, 1) AS int) % 32 + 1, 1);
            SET @i += 1;
        END
        SET @code = CONCAT(N'TT-', @y, N'-', LEFT(@rand, 3), N'-', RIGHT(@rand, 3));
        IF NOT EXISTS (SELECT 1 FROM Trips WHERE Code = @code) BREAK;
    END
    UPDATE Trips SET Code = @code WHERE Id = @id;
    FETCH NEXT FROM c INTO @id, @y;
END
CLOSE c; DEALLOCATE c;
```

Order: add column nullable -> backfill -> alter to NOT NULL -> add unique
index. A row-by-row loop is fine at this app's scale. Hand-authored migration
needs its `.Designer.cs` (see `tasks/lessons.md`, 2026-07-29).

### Where the code is surfaced

| Surface | Change |
|---------|--------|
| Trip Details header | Code shown under the title with a **Copy** button. Read-only text, never an input. |
| Trips list | New "Code" column. `Search` also matches the code, including just the 6-character random part (`7K4Q9M` or `7K4-Q9M`), after normalization. |
| Direct lookup | `GET /Trips/Code/{code}` normalizes, finds the trip, **enforces `TripAccess`**, and redirects to Details. Unknown or not-visible returns 404 (same response, so codes can't be probed). |
| Emails | Added to the submitted / approved / rejected emails. Will also be exposed as `{{Trip.Code}}` once ADR-0005 templates land. |
| Calendar (ICS) | `Trip code: ...` is the first line of every event `DESCRIPTION` (the `SUMMARY` stays purpose + city). **UID stays `trip-{Id}@...`**; changing it would duplicate events already in people's calendars. |
| Approvals queue | Code shown next to purpose. |
| Exports | No per-trip export exists today. Any future per-trip export must include `Code` as its first column. |

### Security

- The code is an identifier, not a secret or capability. Knowing it grants
  nothing: every lookup still goes through `TripAccess`.
- It is not guessable and does not reveal how many trips exist. It does reveal
  the year the trip was created, which is harmless.

## Alternatives considered

| Option | Why not |
|--------|---------|
| Year + sequence (`TRP-2026-00123`) | Most readable and sortable, but reveals yearly trip volume, is guessable, and needs a per-year counter table with locking (`MERGE ... HOLDLOCK`) plus careful backfill ordering. Gaps from failed saves would also look like missing trips. |
| Random with no year (`TT-7K4Q9M`) | Shorter, but the year gives people useful context at a glance and makes a mistyped code from the wrong year obvious. |
| Derived from `Id` (`TT-000123`) | Zero new logic, but exposes the DB key and couples the reference to storage (a data migration or merge could renumber). |
| GUID | Unique with no checks, but far too long to read or type. |
| Assign on approval | Leaves Draft/Submitted trips with no reference right when people need one. |

## Consequences

**Positive**
- One stable reference per trip, readable and typeable, usable in Concur.
- Not guessable, leaks no volume, no gaps to explain.
- Simple runtime: generate, check, save. No counter table or locking.
- Central assignment covers every creation path, including seeders.
- Existing trips get codes on deploy; no manual step.

**Negative / limits**
- Codes are not in creation order within a year; sort by `CreatedAt` instead.
- Harder to say out loud than a number ("seven K four Q nine M").
- Collision handling adds a lookup per new trip (one indexed query).
- Backfill is a row-by-row T-SQL loop. Fine for thousands of trips; would
  need batching for millions.

## Test plan

- `TripCode` unit tests: format and length, only alphabet characters, year
  taken from `CreatedAt` not `StartDate`, normalization (case, spaces,
  missing hyphens, `O -> 0`, `I`/`L -> 1`), reject malformed input.
- Collision path: with a stubbed RNG that repeats a code, generation redraws;
  after the attempt limit it throws.
- Integration (LocalDB): Create, Clone, and seeders all produce a code; clone
  code differs from source; unique index rejects a forced duplicate.
- Posting a `Code` field to Create/Edit is ignored.
- Lookup route: own trip redirects; other team's trip returns 404; unknown code
  returns 404; lowercase / spaced input resolves.
- Search: full code and random-part-only both find the trip.
- Migration: every existing trip gets a unique, well-formed code with the
  right year; column ends NOT NULL with a unique index.

## Open questions

1. Is `TT` the prefix you want, and should it become configurable later (e.g.
   per company)? If yes, it must be frozen per trip, so old codes keep theirs.
   A. TT is fine
2. Does Concur (or finance) enforce a max length or charset on the field the
   code goes into? `TT-2026-7K4-Q9M` is 15 chars, ASCII, with hyphens.
   We don't know what they may use so this is fine for now
3. Would `TT-2026-7K4-Q9M` (random part split 3+3) be easier to read aloud,
   at the cost of one more character?
   Yes. Implemented: canonical form is `TT-2026-7K4-Q9M`; the hyphen inside the
   random part is optional on input.
4. Should Admins be able to search all trips by code from a global search box,
   or is the Trips list + lookup route enough?
   Trip list + lookup route should be fine for now
