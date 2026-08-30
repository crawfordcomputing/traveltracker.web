# ADR-0002: Soft-Warn (Not Gate) Cost Entry on Not-Yet-Approved Trips

**Status:** Accepted
**Date:** 2026-08-30
**Deciders:** Project maintainer(s)

## Context

M6 introduced a **pre-trip approval gate**. The trip lifecycle is
`Draft -> Submitted -> Approved -> Completed` (plus `Rejected` and `Cancelled`),
and the `TripStatus` enum documents Approved as "cleared for travel." Approval
happens **before** the trip, authorizing the plan (dates, destination, purpose).

Expenses and mileage were built earlier (M3), before the approval gate existed.
Confirmed from the current code:

- `ExpensePageModel` and `MileagePageModel` authorize purely on the parent trip's
  access scope (team reachability). Neither checks `TripStatus`.
- There are **zero** `TripStatus` references in the Expenses or Mileage handlers.
- `Expense.Reimbursed` is documented as "INDEPENDENT of the trip's status."

**Result:** a user can add expenses and mileage to a trip in any status,
including a `Draft` trip that has never been submitted. This is confusing because
the app's model is "approve the plan, then travel, then record actuals."

### The distinction that matters

Two coherent models exist:

1. **Plan-only approval** (what the schema implements). Approval signs off on the
   itinerary; expenses are pure post-approval actuals. There is no estimate/actual
   flag anywhere on `Expense`.
2. **Budget approval.** Approval signs off on a proposed spend, which needs
   estimated lines entered pre-approval plus an actuals distinction. The schema
   does not model this.

We are staying in model 1. A budget/estimate feature is **explicitly out of scope**
for this decision.

### Why not a hard gate

A hard "no costs until Approved" rule is tempting but wrong at the edges:

- **Prepaid bookings** (airfare, hotel, conference fees) are legitimately paid
  between approval and travel. A hard block keyed to travel completion breaks them.
- **Slow approvers.** If a trip sits in `Submitted` for days, users will need to
  record real spend and would otherwise work around a block.
- Mileage is the one clear-cut case (it cannot physically exist before travel),
  but it is not worth a separate, stricter rule.

### Related prior art in the codebase

The app already uses **advisory, non-blocking** signals rather than hard gates:

- `ExpensePolicyCheck` renders an "Over guideline" badge from a hardcoded
  per-category soft-cap table (Meals $75, Lodging $350, Entertainment $150,
  GroundTransport $100). Advisory only; never blocks submission. (Itself a
  documented placeholder for a future configurable `ExpensePolicy` table.)
- The duplicate-expense check is confirm-once, not a block.
- Missing-receipt affidavits are an escape hatch, not a block.

A soft warning for cost entry fits this established pattern.

## Decision

Keep expense and mileage entry **ungated** by trip status. Add a **soft,
advisory, non-blocking warning banner** to the cost-entry pages
(`Expenses/Create`, `Mileage/Create`) shown when the parent trip is **not yet
approved**.

- The "cleared for costs" states are `Approved`, `Completed`, and legacy `Planned`
  (which behaves like Approved). No banner shows for those.
- All other states (`Draft`, `Submitted`, `Rejected`, `Cancelled`) show a
  status-specific advisory message. Saving is never prevented.
- The rule lives in one place, `TripStatusRules.CostEntryWarning(status)`, so the
  UI and any tests agree, matching how transition rules are already centralized.

Budget/estimate modeling is deferred. This ADR does not change the schema.

## Options Considered

1. **Do nothing.** Cheapest, but leaves the confusing UX the maintainer flagged.
2. **Hard gate (block cost entry until Approved).** Consistent with the plan-only
   model but breaks prepaid bookings and creates friction during slow approvals.
   Rejected.
3. **Soft warning (chosen).** Preserves legitimate pre-approval spend, nudges the
   correct order of operations, and matches the app's existing advisory-signal
   pattern. Low risk, no migration.

## Consequences

**Positive**
- Clears up the "why can I expense a Draft trip?" confusion without new blocks.
- One centralized, tested helper; trivial to escalate to a hard gate later if
  usage shows it's warranted.
- No schema change, no migration.

**Negative / limits**
- A warning is ignorable; it does not enforce process. Accepted by design.
- Scope is the **Create** (entry) pages only. Editing an existing line on a
  not-yet-approved trip does not warn (low-value; easy follow-up if wanted).
- Does not address the separate, related placeholder nature of
  `ExpensePolicyCheck` (guidelines are hardcoded, not configurable). Out of scope.

## Follow-ups (not part of this change)

- Optional: extend the warning to the Edit pages.
- Future: configurable `ExpensePolicy` table (replaces the hardcoded guideline
  caps) and, if ever desired, a budget/estimate feature that would revisit the
  gate-vs-warn decision.
