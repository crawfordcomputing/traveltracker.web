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
