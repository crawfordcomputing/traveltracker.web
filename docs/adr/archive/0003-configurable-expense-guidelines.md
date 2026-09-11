# ADR-0003: Configurable Per-Category Expense Guidelines

**Status:** Accepted (implemented; verified 2026-09-11). Archived.
**Date:** 2026-08-30
**Deciders:** Project maintainer(s)

## Context

`ExpensePolicyCheck` drove the "Over guideline" badge on the trip expense list from
a **hardcoded** per-category cap table (Meals $75, Lodging $350, Entertainment $150,
GroundTransport $100). Changing a guideline meant editing code and recompiling, and
its own comment marked it a placeholder for a future configurable table. Admins had
no way to see or change these values.

The guideline is, and stays, **advisory**: it flags a line but never blocks entry or
submission (consistent with ADR-0002 and the app's other soft signals).

## Decision

Introduce a configurable, admin-managed guideline table.

- **Entity `ExpensePolicy`**: one row per `ExpenseCategory` (unique), holding a
  `CapAmount` in base currency plus an optional note. A category with no row is
  uncapped, preserving the previous dictionary semantics exactly.
- **Single current value, not effective-dated.** The badge is computed live and
  nothing is frozen onto past expenses, so history is unnecessary. Editing a cap
  changes the guideline going forward. (Contrast MileageRate, which is effective-
  dated because each mileage entry freezes the rate at its date.)
- **`ExpensePolicyCheck` stays a pure static helper.** It now takes a
  `(category -> cap)` lookup instead of a hardcoded table; the page loads the rows
  and passes them in, matching the app's "pure domain logic, pages load data"
  convention. API and message format are unchanged.
- **Admin UI** at `/Admin/ExpensePolicies` (under `RequireAdmin`), modeled on the
  Mileage rates page: list with inline amount edit, add for any not-yet-capped
  category, and delete (which simply makes that category uncapped again). A card is
  added to the Admin home.
- **Seeded defaults via `HasData`.** The four previous hardcoded caps are seeded in
  the migration so behavior is identical the moment this ships; admins own them
  thereafter.

Scope is per-category cap amounts only. Receipt thresholds and any submission-
blocking policy are explicitly out of scope (see Follow-ups).

## Consequences

**Positive**
- Guidelines are visible and editable by admins with no code change.
- Behavior is unchanged on deploy (seeded defaults), then fully configurable.
- Pure-helper refactor keeps the logic unit-testable; caps are just data.

**Negative / limits**
- `HasData` seed rows carry fixed Ids and a fixed `UpdatedAt` constant; changing the
  seed later generates EF `UpdateData`. Acceptable for initial defaults.
- Still advisory only. Making guidelines enforceable is a separate decision.
- Base currency only; no per-department or per-role variation.

## Follow-ups (not part of this change)

- Optional per-category receipt-required threshold (currently a global config value).
- Optional "block on submit when over guideline" flag, if enforcement is ever wanted
  (would revisit the advisory stance).
- Per-department or per-grade guideline overrides.
