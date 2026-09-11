# Architecture Decision Records

New ADRs go in this folder as `NNNN-short-title.md`. Next number: **0006**.

When an ADR is fully implemented (or rejected), set its final status and move it to [`archive/`](archive/).
Code comments cite ADRs by number (e.g. "See ADR-0004"), so numbers are never reused.

## Active

_None._

## Archived

| ADR | Title | Outcome |
|-----|-------|---------|
| [0001](archive/0001-database-provider-selection.md) | Selectable database provider (SQLite or SQL Server) | Rejected. SQL Server only. |
| [0002](archive/0002-cost-entry-approval-soft-warning.md) | Soft-warn (not gate) cost entry on not-yet-approved trips | Implemented |
| [0003](archive/0003-configurable-expense-guidelines.md) | Configurable per-category expense guidelines | Implemented |
| [0004](archive/0004-read-only-trip-reference-code.md) | Read-only trip reference code (year + random) | Implemented |
| [0005](archive/0005-admin-editable-email-templates.md) | Admin-editable email templates (internal / external) | Implemented |
