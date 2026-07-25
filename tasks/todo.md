# M5 Polish — Cost Center + Project Code as managed reference data

Covers the first three M5 checklist items (they're one coupled unit):
1. Add "Cost Center" table; Admin/Finance manage it
2. Add "Project Code" table; Admin/Finance/Manager manage it
3. Convert Trip.ProjectCode / Trip.CostCenter + profile DefaultCostCenter from free-text to dropdowns backed by the new tables

## Design decisions (proposed — confirm before build)

- **Entities.** `CostCenter { Id, Code (required, unique), Name, IsActive }` and
  `ProjectCode { Id, Code (required, unique), Name, IsActive }`. Code is the short token
  (e.g. `ENG-100`); Name is the human label. `IsActive` lets an org retire a code without
  deleting history (dropdowns show active only; existing trips keep their link).
- **Trip storage → FK.** Replace `Trip.CostCenter`/`Trip.ProjectCode` (string) with
  `CostCenterId`/`ProjectCodeId` (int?, FK, `DeleteBehavior.SetNull`) + nav props — mirrors
  the `AppUser.DepartmentId` pattern. `AppUser.DefaultCostCenter` → `DefaultCostCenterId` (FK, SetNull).
- **Existing free-text values are dropped.** App is pre-release (dev/eval); no production data
  to migrate. Old string columns are removed in the migration. (If you'd rather preserve/convert
  existing values into seed rows, say so — that's the one real fork here.)
- **Authorization.** New policies: `RequireCostCenters` = Finance/Admin; `RequireProjectCodes`
  = Finance/Manager/Admin. Management pages live under a new `/Reference` folder, authorized
  per-page (`AuthorizePage`) since the two pages differ. Not under `/Admin` (that's Admin-only).
- **No SDK in sandbox** → migration + snapshot + Designer hand-authored (same as M1.5/M3 slices);
  `dotnet build`/`dotnet test` left as a local gate.

## Tasks

- [ ] Entities: `Data/Entities/CostCenter.cs`, `ProjectCode.cs`
- [ ] `AppDbContext`: DbSets + config (unique index on Code, FK behaviors, precision n/a)
- [ ] `Trip.cs`: swap string fields → `CostCenterId`/`ProjectCodeId` + nav
- [ ] `AppUser.cs`: `DefaultCostCenter` → `DefaultCostCenterId` + nav
- [ ] Migration `AddCostCentersAndProjectCodes` (+ Designer + snapshot update)
- [ ] `ReferenceLists`: `CostCenterSelectListAsync` / `ProjectCodeSelectListAsync` (active-only, ordered by Code)
- [ ] Pages `/Reference/CostCenters/Index` + `/Reference/ProjectCodes/Index` (list + add + activate/deactivate + delete), Departments-style
- [ ] `Program.cs`: `AuthorizePage` for the two new pages; `AuthSetup`: two new policies
- [ ] Trips Create/Edit: `<select>` instead of `<input>`; bind `CostCenterId`/`ProjectCodeId`; prefill default cost center from profile
- [ ] Trips Details/Index: show Code (+ Name) via nav
- [ ] Profile: `_TravelerProfileFields` + `TravelerProfileInput` → default cost center dropdown
- [ ] `ReportAggregation` / ReportPageModel: bucket by cost-center Code via nav (keep report output stable)
- [ ] Nav links + Admin/Index (or a Reference landing) cards, role-gated
- [ ] DevSeeder: a few sample cost centers + project codes; wire existing seeded trips to them
- [ ] Tests: entity uniqueness, select-list active/order, page create/duplicate/deactivate, Trip binds FK, report bucketing unchanged

## Verification (local gate — no SDK here)
- `dotnet build -c Release` 0/0; `dotnet test` green
- `dotnet ef migrations has-pending-model-changes` to confirm snapshot matches model
- Smoke: Finance user manages cost centers but not (Manager-only) — confirm policy gating; Trip create shows dropdowns; report still buckets by cost center

## Review — Cost Center + Project Code reference data (2026-07-24)

Shipped M5 items 1–3 as one unit. Free-text `Trip.CostCenter`/`ProjectCode` and
`AppUser.DefaultCostCenter` are now FK links into two managed reference tables.

**Data model**
- New entities `CostCenter` / `ProjectCode` (`Id`, unique `Code`, optional `Name`, `IsActive`).
- `Trip.CostCenterId` / `ProjectCodeId` and `AppUser.DefaultCostCenterId` FKs, all
  `DeleteBehavior.SetNull` (mirrors `Department`) so retiring a code never erases history.
- Migration `20260724120000_AddCostCentersAndProjectCodes`: creates both tables, drops the
  three old string columns (pre-release, values intentionally dropped), adds FK columns +
  indexes. Hand-authored migration + Designer + snapshot (no SDK in sandbox). Down reverses.

**Management UI + access**
- `/Reference/CostCenters/Index` (policy `RequireCostCenters` = Finance/Admin) and
  `/Reference/ProjectCodes/Index` (policy `RequireProjectCodes` = Finance/Manager/Admin),
  authorized per-page in `Program.cs` (not under the Admin-only `/Admin` folder).
- Each page: add (unique-code validated), deactivate/reactivate, delete (nulls references first).
- Nav links role-gated in `_Layout`; cards added to `Admin/Index`.

**Dropdown conversion**
- `ReferenceLists.CostCenterSelectListAsync` / `ProjectCodeSelectListAsync`: active-only,
  ordered by Code, but include the currently-selected id even if inactive; label = "Code — Name".
- Trips Create/Edit use `<select>` bound to the FK ids (+ posted-id existence validation);
  Create prefills the traveler's default cost center. Details shows the linked `.Code`.
  Index Clone copies the FK ids. Reports project `t.CostCenter?.Code` so aggregation/CSV are
  unchanged.
- Profile default cost center is a dropdown (`TravelerProfileInput.DefaultCostCenterId`, shared
  `_TravelerProfileFields` partial fed by `ViewData["CostCenters"]` from Profile + Admin/Users/Edit).
- DevSeeder seeds sample cost centers/project codes and links them on first seed only.

**Verification (run via Desktop Commander on the dev box):**
- `dotnet build` (warnings-as-errors) **succeeds**; `dotnet test` **313/313 green**.
- `dotnet ef migrations has-pending-model-changes` → "No changes since the last migration"
  (hand-authored migration + snapshot match the model).
- New `M5ReferenceDataTests` (select-list rules, page create/duplicate/toggle/delete-nulls-refs,
  Trip create prefill) plus updated `TripCloneTests` / `TripPagesTests` / `ProfilePageTests`
  (now FK-based / new ctor arg).
- Runtime bug fixed during verification: `ReferenceLists` projected into a record *before*
  the `Where`, which EF couldn't translate ("The LINQ expression … cannot be translated"). Now
  filters/orders on scalar columns, then formats labels in memory.
- Also refactored the profile cost-center picker to expose a `CostCenterOptions` page property and
  set `ViewData["CostCenters"]` from the *view* (not the page model) so it doesn't NRE in the
  direct-PageModel test harness.
- Still worth a manual smoke on deploy: Finance sees Cost centers nav but a Manager does not;
  Manager sees Project codes; deactivated code hidden from picker but still shown on an existing
  trip. Apply migration on deploy.

**Remaining M5 (next, moving down the list):** email on approval decision, empty-states/validation/
a11y pass, seed/demo toggle, contributing guide + issue templates, OCR receipts, dynamic per diems.

---

# M5 — Empty states, validation, accessibility pass (line 205) — 2026-07-25

## Context
Line 204 (email on approval decision) skipped: it depends on the M6 approval flow
(Submitted/Approved/Rejected statuses + approve/reject handler), which is deferred
and unbuilt. User chose to pick up line 205 instead.

## Audit findings
- **Empty states:** already present on almost every list (Trips/Index, WhoIsOut,
  CostCenters, ProjectCodes, home dashboard, Expenses/Split via `Count > 0`). Only
  gaps: Admin/Users/Index (no guard) and Trips/Calendar (no "no trips" hint).
- **Validation:** input forms already have validation-summary + asp-validation-for +
  `_ValidationScriptsPartial`. The POST forms lacking a summary are delete-confirm /
  single-action posts with no user input — correctly need none. No churn planned.
- **Accessibility:** the real, systematic gap.
  - `_Layout` has `lang`, `<main role="main">`, aria on toggler/brand, but **no
    skip-to-content link** and no `id` on `<main>`.
  - Data-table `<th>` cells lack `scope="col"` (only CostCenters/ProjectCodes have
    it), no `<caption>`, and empty action-column headers are unlabeled.

## Plan
- [ ] 1. `_Layout.cshtml`: `.visually-hidden-focusable` skip link (first body child)
      → `#main-content`; add `id="main-content"` to `<main>`.
- [ ] 2. Table a11y across data tables (Trips/Index, WhoIsOut, Admin/Users,
      Admin/MileageRates, Admin/Invites, Admin/Arrangers, Calendar, Expenses/Split,
      Reports, Trips/Details): `scope="col"` on headers, visually-hidden `<caption>`,
      visually-hidden label on empty action `<th>`.
- [ ] 3. Empty states: Admin/Users/Index `Count==0` guard; Calendar month hint.
- [ ] 4. Verify: sandbox build (0 warn / 0 err), grep-verify coverage, no validation
      regressions. List (don't run) tests per deferred-testing rule. Update this file.

## Principles
Minimal impact, no schema/migration change, no new dependency — pure markup/CSS.

## Review — Empty states / validation / a11y pass (2026-07-25)

Skipped line 204 (email on approval decision): depends on the unbuilt M6 approval
flow. Did line 205 instead.

**Accessibility (the real gap)**
- `_Layout.cshtml`: added a Bootstrap `.visually-hidden-focusable` skip link as the
  first body child, targeting `#main-content` (added `id` + `tabindex="-1"` to
  `<main>`). Skip-link CSS + a focus-outline reset for `#main-content` added to
  `site.css`. Benefits every page for keyboard/screen-reader users.
- Data tables: added `scope="col"` to every column header, a `visually-hidden`
  `<caption>` naming each table, and `visually-hidden` "Actions" text on the
  previously-empty action-column headers. Covered Trips/Index, WhoIsOut,
  Admin/Users, Admin/MileageRates, Admin/Invites, Admin/Arrangers, Calendar,
  Expenses/Split, Reports (4 tables), Trips/Details (3 tables), plus captions on
  CostCenters/ProjectCodes (already had scope).
- Totals rows: `scope="row"` on the "Total"/"Reimbursable"/"Total mileage" row
  headers; converted empty spacer `<th></th>` cells to `<td>` (an empty `<th>` is
  a meaningless header).

**Empty states**
- Admin/Users/Index: added a `Count == 0` colspan row (mirrors the Invites pattern).
- Trips/Calendar: added a "No trips overlap {month}" hint when the month is empty.
- Audit confirmed every other list already had an empty state; no other changes.

**Validation**
- No churn: input forms already carry `validation-summary` +
  `asp-validation-for` + `_ValidationScriptsPartial`. The POST forms without a
  summary are delete-confirm / single-action posts (no user input) — correctly
  need none.

**Verification**
- No .NET SDK in this sandbox (build/test is the local Windows gate, as in prior
  milestones). Changes are pure Razor markup + CSS — no code/DI/schema touched.
- Grep-verified: 0 bare `<th>` remain in the data tables; 12 pages carry captions;
  skip link + `#main-content` present; `visually-hidden-focusable` exists in the
  bundled Bootstrap CSS.
- The large `git diff --stat` is the known CRLF-vs-LF artifact (see lessons.md),
  not real changes — do not "fix" line endings.

**Should be tested locally (deferred per CLAUDE.md — not run here):**
- `dotnet build -c Release` → expect 0 warnings / 0 errors (Razor compiles).
- `dotnet test` → existing suite should stay green (no logic changed).
- Manual: Tab from page load shows the skip link and it jumps focus to content;
  a screen reader announces table captions + column headers; Admin/Users and an
  empty Calendar month show their empty states.

---

# M5 — Seed/demo data toggle + richer demo data (line 206) — 2026-07-25

## Decisions (user-confirmed)
- **Toggle stays dev-only.** The toggle already existed (`Seed:DevData` in
  appsettings.Development.json, gated in Program.cs by `#if DEBUG` +
  `IsDevelopment()`). Keeping it dev-only so demo accounts with the shared
  `Password123!` never ship in a Release artifact. No gating change.
- **No receipt files.** `ReceiptPath` is an opaque `IReceiptStorage` key; seeding
  real files was out of scope. Instead, two over-threshold receipt-less lines carry
  a `MissingReceiptAffidavit` to exercise that path.

## What changed (`Data/DevSeeder.cs` only)
Previously expenses + mileage existed on **one** trip (Ethan's completed onsite).
Added three small idempotent helpers (`TripOf`, `SeedExpensesAsync`,
`SeedMileageAsync` — each no-ops if the trip already has rows) and seeded:
- **Evan / Cloud Summit 2026** (Planned, arranger-booked): airfare, lodging,
  conference registration, Lyft, + a kiosk meal with an affidavit. Flew → no mileage.
- **Ella / Q3 client visit** (Planned, self): airfare, lodging, a client dinner
  (attendees + business purpose), a personal in-room line (excluded from
  reimbursables), airport parking with an affidavit; + round-trip mileage.
- **Morgan / Leadership offsite** (Planned, self): airfare (already reimbursed →
  mixed reimbursement state), lodging, a team dinner; + round-trip mileage w/ commute.

Mileage on the two new drives is dated ≥ Jul 1 2026, so it freezes at the **76¢ H2
2026** rate — distinct from Ethan's 72.5¢ H1 entry, showing the effective-dated
rate freeze in action.

## Verification
- Grep-verified brace/paren balance in `DevSeeder.cs`; signatures match existing
  `MileageMath` / `MileageRateResolver` calls.
- No test regression risk: integration tests boot with `Seed:DevData=false`
  (AuthorizationTests, HealthEndpointTests), and `DevSeeder` is `#if DEBUG` only.
- No SDK in sandbox — build/test remains the local gate.

**Should be tested locally (deferred):**
- `dotnet build` (Debug) → DevSeeder compiles.
- Run the app in Development with `Seed:DevData=true` on a fresh DB → confirm
  4 trips carry expenses, 3 carry mileage, reports/dashboard totals populate, and
  the two affidavit lines render. Re-run startup → no duplicates (idempotent).
