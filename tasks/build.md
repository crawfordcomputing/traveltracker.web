# Travel Tracker — Build Plan

Open-source business travel tracker. One ASP.NET Core app, deployable to Azure App Service via Azure DevOps as simply as possible.

## Decisions (locked)

| Area | Choice | Notes |
|------|--------|-------|
| Scope | Full blend | Trips + approvals + expenses + mileage from MVP |
| Backend | ASP.NET Core (.NET 8 LTS) | Single deployable artifact |
| Frontend | Razor Pages / MVC | Server-rendered, no JS build step, contributor-friendly |
| ORM | EF Core | Provider-agnostic |
| DB | SQLite default, Azure SQL via config switch | One connection-string + provider setting |
| Auth | Pluggable | ASP.NET Core Identity (local) + optional Entra ID |
| CI/CD | Azure DevOps `azure-pipelines.yml` | build, test, publish, deploy |
| Infra | Optional `main.bicep` | App Service (+ optional Azure SQL) |
| License | MIT (open source) | Confirmed |
| Currency | Single currency for MVP | Multi-currency deferred |
| Receipt storage | Local disk default, Azure Blob via config switch | Mirrors the SQLite -> Azure SQL pattern |

## Known trade-off: SQLite on App Service
- SQLite file lives in `/home` (persisted) but only ONE instance can safely write, so no scale-out.
- Deploy-from-zip must NOT overwrite the DB file — keep DB outside the app publish folder / use `/home/data`.
- Production users flip one setting to Azure SQL. EF Core migrations work against both.

## Data Model
- **User** (Identity user, FK Department, Role: Employee/Manager/Admin)
  - *(M6, deferred: `ApproverId` self-reference — who approves this user's trips; not necessarily the direct manager)*
- **Department** (name; reporting rollups)
- **Trip** (traveler, purpose, status: Draft/Planned/Completed/Cancelled, start/end — approval statuses added in M6)
- **Destination** (FK Trip; city, country, arrive/depart — supports multi-leg)
- **Approval** (FK Trip, approver, decision, comment, timestamp) — *deferred to M6*
- **Expense** (FK Trip, category, amount, currency, base-currency amount + captured FX rate & rate date, receipt file path, date, `IsPersonal`/non-reimbursable flag, `ReimbursedAt`, attendees + business purpose for meals/entertainment)
  - **ExpenseSplit** (FK Expense; category, amount) — one receipt split across categories/trips; splits must sum to the parent amount
- **MileageEntry** (FK Trip, date, miles/km, rate, computed amount, round-trip toggle, commute-deduction miles, ordered waypoints)
  - **MileageWaypoint** (FK MileageEntry; sequence, label) — supports A→B→C legs, not just point-to-point
- **MileageRate** (effective-date, jurisdiction/country, vehicle type, rate) — historical rate table; entries freeze the rate in effect at the mileage date
- **ExpensePolicy** (category, threshold, receipt-required toggle, itemization-required toggle, required-fields rule) — drives policy-limit flagging and submission blocking

## Milestones (shippable slices)

### M0 — Skeleton + pipeline proving ground
- [x] `dotnet new` Razor Pages app, solution + test project structure
- [x] EF Core wired with SQLite; provider switch (`Database:Provider` config) for Azure SQL
- [x] Migrations run on startup; seed an admin + sample department
- [x] `azure-pipelines.yml`: restore/build/test/publish/deploy to App Service
- [x] Deploy a "hello" page to prove the pipeline end-to-end FIRST
- [x] `README` with run-locally + deploy-to-Azure sections
- [x] MIT LICENSE, .gitignore, .editorconfig

### M1 — Auth + roles
- [x] ASP.NET Core Identity (local email/password), register/login/logout
- [x] Roles: Employee / Manager / Admin; role-based page authorization
- [x] Entra ID as optional config toggle (documented, off by default)
- [x] Admin: manage users + departments

### M1.5 — Auth hardening + real-org access model (post-MVP)

**Design theme: model access the way a travel org actually works, not the way IT draws it.** The shipped M1 is a clean skeleton (local Identity, three flat roles, optional Entra) but it assumes one role per person, global managers, and open self-signup. A veteran org has arrangers acting for execs, finance separate from IT, managers scoped to their team, and travelers who onboard a passport once. Close the security holes first, then seat the org relationships M6/M7 will build on.

_Must-haves (real security gaps in the shipped M1)_
- [x] Lock down self-registration: config-driven `Auth:Registration:Mode` (Open/Domain/Invite/Closed, fails closed). `RegistrationPolicy` helper gates the Register page; Domain allowlist + full Invite flow (`Invitation` entity, hashed one-time tokens, admin `Admin/Invites` UI, redemption on Register) shipped. Register nav link hidden when self-serve is off. Base config = Domain (empty allowlist); Development = Open. See `tasks/m1.5-auth-draft.md`. **Local gate pending: `dotnet build` + `dotnet test` (sandbox had no .NET SDK).**
- [x] Self-service password reset (forgot-password → emailed token → reset) **and** change-password for signed-in users. `IEmailSender` abstraction with a config switch (`Email:Provider`): `LoggingEmailSender` default logs the reset link to the console (frictionless dev, no SMTP), `SmtpEmailSender` for prod via `Email:*`. `ForgotPassword` sends only for an existing **active** account and always shows a neutral confirmation (no account enumeration); `ResetPassword` redeems the base64url token; `Manage/ChangePassword` (folder-authorized) rotates a signed-in user's password and refreshes the sign-in. Login page gains a "Forgot your password?" link. Admins can also reset any user's password from `Admin/Users/Index` ("Reset password" per row): mints a strong one-time password (shared `Domain/TempPassword`), sets it via Identity's reset flow (which rotates the security stamp, killing that user's live sessions), and shows it once. Registration/invite flow left unchanged. See `tasks/m1.5-auth-draft.md` §4.
- [x] User deactivation / offboarding: `AppUser.IsActive` flag (migration `AddUserIsActive`, `defaultValue: true` so legacy users stay enabled). `AppSignInManager.CanSignInAsync` refuses inactive accounts (blocks new logins); security-stamp revalidation shortened to 1 min + `UpdateSecurityStampAsync` on toggle kicks live cookies. Deactivate/Reactivate action + Active/Inactive badge on `Admin/Users/Index`.
- [x] Last-admin protection: `Domain/AdminGuard.IsLastActiveAdminAsync` blocks deactivating (Users/Index) or demoting (Users/Edit) the final active Admin. (Deletion is already impossible — no hard-delete path exists.)
- [x] Optional TOTP MFA (opt-in per user; enforce-for-Admins toggle). Authenticator
      enrollment (server-side QR via QRCoder + manual key), verify → enable → one-time
      recovery codes; login gains a second-factor + recovery-code step; `Disable2fa` /
      regenerate codes. `Auth:Mfa:RequireForAdmins` corrals un-enrolled Admins to setup
      via `MfaEnforcementMiddleware` (registered only when on).
- [x] Cookie/session hardening: `Auth:Session` config — idle timeout via
      `ExpireTimeSpan` + sliding renewal (default 30 min) and a hard absolute cap
      (default 8h) stamped at sign-in and enforced in `OnValidatePrincipal` (chained
      before the security-stamp validator so the deactivation kick survives). Cookie
      hardened to HttpOnly / SameSite=Lax / Secure=Always.

_Role model (too flat today — single role, global managers)_
- [x] Add a **Finance** role: role + `RequireFinance` policy (Finance or Admin) so finance can see expenses/reports (M3/M4) without account/system god-mode (that stays Admin). Added to `Roles.All`, so it seeds and appears in every role dropdown automatically. Behaviour gating waits on M3/M4 surfaces to protect. See `tasks/m1.5-auth-draft.md` §3.
- [x] Add a **Travel Arranger / Delegate** role as a *scoped* assignment: an arranger acts only for assigned travelers, replacing the all-or-nothing "Manager/Admin can act for anyone." `ArrangerAssignment` table (unique `(ArrangerId, TravelerId)`, Restrict FKs, migration `AddArrangerAssignments`); access decisions stay pure in `TripAccess` (`CanAccess(user, trip, delegated?)` + `Scope`/`TripScope`), fed by the thin `ArrangerAccess` DB lookup (no query for non-arrangers). Scoping threaded through every trip surface (per-trip Edit/Delete/EditLeg/Details/Clone + list/calendar/dashboard/Create picker). Full `Admin/Arrangers` assignment UI + dashboard card.
- [x] Scope Managers to their Department instead of the global `RequireManager` (managers should see their team, not every traveler). Global reach is now **Admin-only** (`TripAccess.CanManageGlobally`); a Manager is a *scoped* role like Arranger, reaching every other member of their `Department` (fails closed when they have none). The thin `ArrangerAccess` DB lookup became `TeamAccess.ReachableTravelerIdsAsync` (arranger assignments ∪ manager's department); pure `TripAccess.Scope`/`CanAccess` unchanged in shape. "Who's out" is now scoped to the manager's department (Admin still org-wide). Cost center is a per-trip tag, not an org membership, so Department is the scoping unit.
- [x] Seat the org relationship now (reports-to / approver) even though the approval flow is M6, so duty-of-care and M6 aren't a retrofit. (Aligns with the deferred `ApproverId` self-FK.) `AppUser.ApproverId` self-FK (Restrict, migration `AddUserApprover`) + `Approver` nav; assign on `Admin/Users` Create/Edit (approver dropdown of active users, excludes self on Edit) with self- and cycle-rejection via pure `Domain/ApproverGraph`; Approver column on the Users list; `ReferenceLists.ApproverSelectListAsync`; DevSeeder seats a two-level chain (employees → Morgan → Alex). Relationship only — submit/approve stays M6. **Local gate pending: `dotnet build` + `dotnet test` (sandbox has no .NET SDK).**

_Traveler profile depth (capture once at onboarding; feeds M3/M7)_
- [x] Travel-document fields on the profile: passport number + **expiry** + nationality, Known Traveler Number / TSA PreCheck / Global Entry, mobile number, emergency contact. Passport & KTN numbers stored **encrypted at rest** via `Services/SensitiveFieldProtector` (ASP.NET Data Protection); everything else plaintext PII. Fields live on `AppUser` (migration `AddTravelerProfile`).
- [x] Loyalty/frequent-flyer numbers + seat/meal/hotel preferences (dormant until M7 booking, stored now).
- [x] Defaults that stop per-trip retyping: default cost center, reimbursement method (`ReimbursementMethod` enum). Default approver = the user's `ApproverId` (seated above), so it isn't duplicated here.
- [x] Passport-expiry warning surfaced in the duty-of-care view (free win once expiry is stored). Pure `Domain/PassportExpiryStatus` (6-month threshold) drives an Expired / Expires-soon badge in the "Who's out" roster.

Editing surface: self-service `/Account/Manage/Profile` (traveler captures their own) **and** `Admin/Users/Edit`, both sharing `Models/TravelerProfileInput` + the `_TravelerProfileFields` partial. **Local gate pending: `dotnet build` + `dotnet test` (sandbox has no .NET SDK).**

_Entra ID → actually enterprise-grade (today it logs in but roles are still hand-managed)_
- [x] Map Entra/AD groups to app roles (JIT role assignment on login). Config-driven `Auth:EntraId:GroupRoleMappings` (array of `{GroupId, Role}`, many-to-many) reconciles a user's roles against their `groups` claim on every external login. Pure `Domain/EntraRoleMapper` computes the add/remove plan (only roles that appear as a mapping *target* are ever revoked, so hand-granted roles like Admin survive); `Services/EntraRoleSynchronizer` applies it via `UserManager`, refuses to strip Admin from the last active admin (`AdminGuard`), and bumps the security stamp so changes take on the next request. Wired into both `ExternalLogin` paths (returning user → `RefreshSignInAsync`; fresh provision → synced before the cookie is minted). No-op unless Entra is on **and** mappings exist, so enabling Entra never disturbs existing manual roles. `SyncRolesOnLogin` (default true) + `GroupClaimType` (default `groups`) config switches; token-overflow overage claim is detected and logged. **Recommended mode: `RoleClaimPassthrough=true`** — name the Entra *app roles* to match ours (`Employee`/`Arranger`/`Manager`/`Finance`/`Admin`) and the `roles` claim value is honored directly with no mapping table (directory owns the whole role model; Employee is a permanent floor; no group-overflow risk). See `tasks/m1.5-auth-draft.md` §5.
- [ ] SCIM auto-provisioning/deprovisioning so AD offboarding revokes access here automatically.
- [x] Turn on email confirmation for prod. Now config-driven: `Auth:RequireConfirmedEmail` (default false, so dev/eval and tests stay frictionless) feeds `SignIn.RequireConfirmedAccount` in `AuthSetup`. Shared `Services/EmailConfirmationService` mints the token, base64url-encodes it (same shape as the ForgotPassword flow), and sends via the existing `IEmailSender`. New `ConfirmEmail` (lands the token → `ConfirmEmailAsync`), `RegisterConfirmation` ("check your email"), and `ResendEmailConfirmation` (neutral, no enumeration; only resends for active+unconfirmed) pages. Self-serve registration emails the link and redirects to `RegisterConfirmation` when confirmation is required, else keeps the immediate sign-in. **Invited users skip confirmation** (`EmailConfirmed=true` — receiving the invite proves ownership) and sign in as before. Login now handles `IsNotAllowed` (correct password, unconfirmed email) with a clear message + resend link instead of "invalid email or password". Seeded admin/dev users already carry `EmailConfirmed=true`, so enabling the flag never locks them out. **Local gate pending: `dotnet build` + `dotnet test` (sandbox has no .NET SDK).**

_Verification_
- [~] Unit tests: last-admin guard ✓, delegate-scope enforcement ✓ (`TripAccess.CanAccess`/`Scope`), scoped-manager reach ✓ (`TripAccess` — Admin global vs Manager department-scoped; `CanManageGlobally`/`CanSeeTeamRoster`), deactivated-user cannot sign in ✓, absolute-session-cap boundary ✓ (`SessionPolicyTests`), authenticator URI/key formatting ✓ (`AuthenticatorUriTests`), Entra group→role mapping ✓ (`EntraRoleMapperTests` — parse drops unknown roles/blank groups, desired-roles from groups, reconcile adds/revokes only managed roles, unmanaged roles untouched, empty mapping no-op).
- [~] Integration test: invite → register via invite ✓; password reset round-trip ✓; deactivate → login denied ✓; arranger sees self + assigned only ✓; manager sees self + department only ✓ (`ArrangerAccessTests` — `Manager_Reaches_Department_Teammates_Only`, `Trips_Index_Scopes_Manager_To_Department`); TOTP enable (valid code enables + mints recovery codes, wrong code rejected) + password sign-in requires the 2nd factor once enabled ✓ (`TwoFactorTests`). Full invite→register→reset chained test still open.

### M2 — Trips (tracking only)
- [x] CRUD trips with multi-leg destinations
- [x] Trip status lifecycle: Draft → Planned → Completed (+ Cancelled). No approval gate — see M6.
- [x] My Trips list; Manager/Admin can create + manage trips on behalf of another traveler
- [x] Dashboard + server-rendered calendar (month grid) view of trips

#### M2 enhancements (post-MVP) — reviewed against shipped code

**Design theme: the itinerary is the source of truth.** A veteran plans in legs, not in two abstract trip-level dates. Derive/validate trip dates from destinations rather than trusting hand-typed values, and capture the details travelers actually chase (confirmation numbers, transport, lodging) so M3 expenses and M4 reporting have something to hang off.

_Must-haves (data integrity — real gaps in the current MVP)_
- [x] Validate `EndDate >= StartDate` on Create/Edit (already present; centralised into `TripDateRules.IsValidTripRange`)
- [x] Leg dates: validate Arrive <= Depart; window integrity handled by reconcile below (itinerary is source of truth, so legs expand the window instead of being rejected)
- [x] Auto-derive (reconcile) trip Start/End to contain all legs on add/remove (`TripDateRules.ReconcileWindow`, expand-only so buffer days survive)
- [x] Warn on overlapping trips for the same traveler (`TripOverlap`; advisory banner on Details, ignores self + cancelled)
- [x] Capture a cancellation reason when moving a trip to Cancelled (required on the cancel action)

_Location reference data (added this pass)_
- [x] Add `State` to Destination; default Country = "United States"
- [x] Prepopulated `Country` (ISO 3166, 249) + `UsState` (50 + DC) reference tables, seeded idempotently at startup
- [x] State + Country rendered as dropdowns on the add-leg form

_High-value tracking depth_
- [x] Per-leg transport mode (flight / rail / car / bus / ferry / other) and lodging name
- [x] Confirmation / booking reference numbers per leg
- [x] Free-text notes on a trip and on each destination
- [x] Cost center / project code + trip type (`TripType` enum) on the trip; shown on Details, feeds M4 rollups
- [x] Duplicate / "clone this trip" for recurring routes (Index Clone action → new editable Draft)
- [x] Edit a leg in place (dedicated EditLeg page, reconciles the window) + Notes column in the itinerary table

_List & findability_
- [x] Filter trips by status and date range; free-text search on purpose/city
- [x] Upcoming / past / all toggle (default upcoming) + pagination (20/page)

_Duty of care (manager value)_
- [x] "Who's out" manager-only view (RequireManager) — travelers with trips overlapping today / next 7 days, with base location and current stop
- [x] Traveler base location + time-zone id on the profile (admin create/edit). Note: itinerary legs are date-only, so local-time display is deferred until leg times exist (M-later)

### M3 — Expenses + mileage

**Design theme: freeze computed values at submission.** FX rate, mileage rate, and derived amounts are captured and stored when entered/submitted, never recomputed live. Get this right in the schema now so M4 reporting and M7 accounting export inherit stable numbers.

_Core_
- [x] Expense line items with categories + receipt upload *(M3a)*
- [x] Storage abstraction (IReceiptStorage): local disk default, Azure Blob via `Storage:Provider` config switch *(M3a)*
- [x] Mileage entries with configurable rate → auto amount *(M3b)*
- [x] Per-trip cost rollup; simple policy-limit flagging *(M3a; advisory only, real ExpensePolicy in M3d)*

_Must-haves (reimbursement correctness)_
- [~] Multi-currency: capture FX rate at expense date, store frozen base-currency amount alongside original currency *(M3a laid the frozen Currency/FxRate/FxRateDate/BaseAmount schema; entry UI DEFERRED per 2026-07-20 — schema stays ready, no migration needed when it lands)*
- [x] Split one expense across multiple categories/trips (ExpenseSplit; splits must reconcile to parent amount) *(M3c; same-trip categories — cross-trip deferred)*
- [x] Mileage as ordered waypoints with round-trip toggle and a commute-deduction field (not just A→B) *(M3b)*
- [x] Personal / non-reimbursable flag on a line item (nets out of amount owed, still reconciles against card feed in M7) *(M3a)*

_High-value, low-effort_
- [x] Duplicate detection: flag matching amount + date + vendor *(M3c; advisory, confirm-once)*
- [ ] Category/threshold itemization rules that block submission until required fields are filled *(deferred to M3d with the configurable ExpensePolicy — submission-blocking out of M3c scope)*
- [x] Attendees + business purpose fields on meals & entertainment *(M3c)*
- [x] Draft autosave + "clone last trip's expenses" *(M3c)*

_Worth considering_
- [x] Expense reimbursement status independent of trip status (lightweight `reimbursed` flag + date; full approval flow stays in M6) *(M3c)*
- [x] Mileage rate table keyed by effective-date and jurisdiction/vehicle; entries freeze the rate in effect at the mileage date *(M3b)*
- [x] Receipt-required policy toggle with a missing-receipt affidavit (typed justification under threshold) *(M3c; config toggle, advisory)*

### M4 — Reporting + exports

**Design theme: read-only aggregation over already-frozen numbers.** M3 froze
`Expense.BaseAmount` and `MileageEntry.Amount` at entry, so M4 never recomputes money —
reports just sum what's stored. No new migration: M4 is read-only over existing tables.

**Access decisions (locked 2026-07-20):** reports expose everyone's spend, so gating
matters more than any prior surface. Org-wide reach = **Finance or Admin** (the
`RequireFinance` seat from M1.5 finally has something to protect). **Managers/Arrangers**
get a *scoped* version of the same reports via the existing `TeamAccess`
reachable-traveler set — not a 403. Plain Employees get no report nav (their own spend is
already on `Trips/Details`). New pure `Domain/ReportAccess` mirrors `TripAccess`:
`CanSeeOrgWide` (Admin/Finance) → `TripScope.Everyone`, else reuse `TripAccess.Scope`.

_M4a — report queries + on-screen reports_ **(built 2026-07-20; build + tests green)**
- [x] `Domain/ReportFilter` (pure): date-range + status/traveler filter parse & validation (From ≤ To, inverted range surfaced not swapped), overlap test `IncludesWindow`, mirrors `TripDateRules` style
- [x] `Domain/ReportAggregation` (pure, from a flat projected row list): trips-by-person (count, travel-days, expense/mileage/combined cost), cost-by-department (distinct traveler count), cost-by-category, cost-by-cost-center/project + grand totals. Reimbursable excludes `IsPersonal`; money = `BaseAmount` + mileage `Amount`
- [x] `Domain/ReportAccess` (pure): org-wide (Finance/Admin) vs scoped (Manager/Arranger via `TripAccess.Scope`) reach
- [x] `/Reports/Index` (folder-authorized, new `RequireReports` policy = Finance/Manager/Arranger/Admin): summary cards + by-person + by-department + by-category + by-cost-center tables, server-rendered, date-range/traveler/status filters via query string. `ReportPageModel` base resolves scope + projects `ReportRow` (expenses/mileage pulled in separate scoped queries keyed by trip id — no Trip entity change). Nav link shown to report-eligible roles
- [x] Unit tests: `ReportAggregationTests` (sums, personal excluded, empty, travel-day math, distinct travelers, cost-center bucketing/order), `ReportFilterTests` (valid/inverted/equal range, overlap matrix, open sides), `ReportAccessTests` (Finance/Admin org-wide, Manager/Arranger scoped, Employee self)

_M4b — CSV export (Excel deferred)_ **(built 2026-07-20; build + tests green)**
- [x] `Domain/CsvWriter` (pure, zero-dependency, RFC-4180 quoting; CRLF rows, UTF-8 BOM for Excel, invariant `Money`/`Number` helpers so a decimal separator never collides with the delimiter)
- [x] `OnGetExport` handler on `/Reports/Index` for all four views (person/department/category/cost-center) → streamed `text/csv` `FileResult`; per-table "Export CSV" links reuse the current filter via `asp-all-route-data`; inverted range redirects back to the page. Same scope + filter as the on-screen tables (one `LoadRowsAsync` path)
- [x] Unit tests: `CsvWriterTests` (comma/quote/newline quoting, quote-doubling, BOM, invariant money)
- [ ] Excel (.xlsx) deferred: revisit with ClosedXML (MIT) only if formatted workbooks are wanted; CSV covers "export" for now (single-artifact / contributor-friendly ethos)

_M4c — dashboard charts_ **(built 2026-07-20; build + tests green)**
- [x] Chart.js 4.4.1 via cdnjs (no build step), minimal vanilla JS reading a server-serialized JSON blob (`<script type="application/json">`), rendered in `@@section Scripts`. Bails cleanly if the CDN/JSON is unavailable
- [x] Charts: spend-by-month (bar), spend-by-category (doughnut), spend-by-department (bar) — fed from the **same `ReportAggregation`** (new `ByMonth` rollup, keyed to trip start month, chronological) so tables and charts never disagree. Placed on the scoped, `RequireReports`-gated Reports page (not the public home dashboard) so spend is never leaked to Employees

_M4d — itinerary .ics export_ **(built 2026-07-20; build + tests green)**
- [x] `Domain/IcsBuilder` (pure, tested): RFC 5545 VCALENDAR, one all-day VEVENT per `Destination` leg (`VALUE=DATE`, exclusive DTEND = depart + 1), stable per-leg `UID` (`trip-{id}-leg-{legId}@traveltracker.local`) so re-import updates, summary `{Purpose}: {City}`, location from city/state/country, transport/lodging/confirmation/notes in description, TEXT escaping + 75-octet line folding. Legless trip falls back to one trip-window event
- [x] `OnGetIcs` handler on `Trips/Details` → `text/calendar` `trip-{id}.ics`, authorized through `TripAccess` via `LoadAuthorizedTripAsync` (out-of-scope trip 404s). "Add to calendar" button in the Details header. Per-trip download only; subscribable per-user feed deferred (token/auth surface)

### M5 — Polish
- [ ] Add "Cost Center" to the database and allow admin/finance to manage the cost center
- [ ] Add "Project Code" to the database, allow admin, finance and managers to to manage the project codes
- [ ] Change the Project Code and Cost Center fields to drop down selections based on the new database tables. Change the default cost center under the user profile to a drop down too
- [ ] Email notification on approval decision
- [ ] Empty states, validation, accessibility pass
- [ ] Seed/demo data toggle for easy eval
- [ ] Contributing guide + issue templates
- [ ] Use Optical Character Recognition to extract data (date, vendor, amount) directly from photos or PDFs, then automatically pair receipts with matching transactions
- [ ] Dynamic Per Diems: Configure location-specific daily allowances for meals and incidentals, automatically adjusting thresholds based on the travel destination

### M6 — Approvals (deferred; split out of the original M2)
- [ ] Approver relationship: add `ApproverId` self-FK on User — the approver for that user's trips (not necessarily the direct manager)
- [ ] Admin UI to assign each user's approver
- [ ] Submit → approver approves/rejects with comment; status transitions (add Submitted/Approved/Rejected)
- [ ] Approval queue for approvers; approval history on the trip
- [ ] Notify traveler on decision (ties into M5 email notifications)
- [ ] Automated Approval Workflows: Route completed trip reports through custom hierarchies (e.g., manager \(\rightarrow \) finance) with instant notifications and in-app sign-offs

### M7 - Syncing and Linking
- [ ] Booking Integration & Travel Itineraries: Sync flight, hotel, and rental car reservations so employees have their live travel schedule in their pocket and the tracker can audit expenses against policy before booking
- [ ]  Accounting Software Integration: Export finalized expense reports and receipts directly into platforms like QuickBooks, Dynamics or Xero to speed up reconciliation and tax filing
- [ ] Automated Credit Card Syncing: Link corporate cards to automatically pull in transactions, preventing manual entry errors and missing expense

## Verification (per CLAUDE.md — prove it works)
- [x] Unit tests: status transitions, trip authorization (mileage/expense math in M3)
- [x] Integration test: create trip → add multi-leg destinations → mark completed (submit→approve flow moves to M6)
- [ ] Pipeline green; smoke-test deployed URL after each milestone

## Resolved
- MIT license confirmed.
- Single currency for MVP; multi-currency deferred to post-MVP.
- Receipt storage: local disk default (dev/eval), Azure Blob via `Storage:Provider` config switch (same pattern as DB provider).

## Review

### M0 complete (2026-07-17)
- Solution: `TravelTracker.Web` (Razor Pages, .NET 8) + `TravelTracker.Tests` (xUnit).
- EF Core provider switch implemented in `Data/DatabaseSetup.cs` (SQLite default, Azure SQL via `Database:Provider=SqlServer`). Verified both paths compile; SQLite path smoke-tested end-to-end.
- `DbInitializer` migrates on startup and seeds a `General` department + `admin@example.com` admin. Idempotent (covered by test).
- Landing page shows live system status (provider + seeded counts); `/healthz` health endpoint added.
- `azure-pipelines.yml`: restore -> build -> test (results published) -> publish -> deploy to App Service on `main`.
- `infra/main.bicep`: Linux App Service (+ optional Azure SQL via `deploySql=true`); wires `/home/data` SQLite path so deploys never overwrite the DB.
- Hygiene: README (run-local + deploy), MIT LICENSE, .gitignore (excludes `data/`), .editorconfig.
- **Verification:** `dotnet build -c Release` -> 0 warnings / 0 errors. `dotnet test` -> 4/4 passing (seed correctness, idempotency, /healthz 200, home page loads). App boots, creates SQLite DB, serves `/` and `/healthz`.

### M2 complete (2026-07-18)
- **Scope change:** approvals removed from M2 (tracking only) and split into a new **M6**. Trip status simplified to Draft/Planned/Completed/Cancelled.
- Entities: `Trip` (traveler, purpose, status, start/end, `CreatedBy`/`CreatedAt` audit for "on behalf of"), `Destination` (multi-leg, ordered by `Sequence`), `TripStatus` enum. FKs to `AppUser` use `Restrict` (no cascade-delete of history); destinations cascade with the trip. Migration `AddTrips`.
- Domain helpers (testable): `TripStatusRules` (legal transition table) and `TripAccess` (owner-or-Manager/Admin).
- Pages under `/Trips` (folder authorized, any signed-in user): Index (My Trips / all-with-traveler-filter for Manager/Admin), Create (traveler picker for Manager/Admin, self for Employees), Details (itinerary + status-transition buttons + add/remove leg via server postback — no JS), Edit, Delete, Calendar (server-rendered month grid). Dashboard folded onto the home page (status counts + upcoming trips). Nav gains Trips + Calendar for signed-in users.
- **Verification:** `dotnet build` 0/0; `dotnet test` 26/26 (10 status-transition cases, 4 access cases, 3 page-handler workflow tests incl. create→multi-leg→complete and cross-user access denial, plus existing). Runtime smoke test in-browser as admin: login → create trip → add leg (Sequence 1) → Draft→Planned (next-state buttons update correctly) → calendar spans Jul 20–24 → home dashboard shows Planned 1 / Upcoming 1.

### M1.5 §3 complete — role model expanded (2026-07-18)
- The flat `Employee/Manager/Admin` model gains two roles. **Finance**: role + `RequireFinance` policy, ready to gate M3/M4 (no surface to protect yet). **Arranger**: a scoped delegate who sees/acts for only their assigned travelers.
- Access decisions stay pure/unit-testable in `TripAccess` (`CanAccess` gained an optional `delegatedTravelerIds`; new `Scope`/`TripScope`); the only DB piece is `ArrangerAccess`, a one-query lookup that short-circuits (no query) for non-arrangers. New `ArrangerAssignment` table + migration `AddArrangerAssignments`.
- Scoping threaded through per-trip checks and every listing surface (Trips/Index, Calendar, dashboard) + the Create picker/validation; a `ShowTraveler` flag drives the traveler column/picker while "Who's out" stays gated to real global reach. Full `Admin/Arrangers` assignment page (RequireAdmin folder) + dashboard card.
- **Verification:** `dotnet build -c Release` 0/0; `dotnet test` 97/97 (+11). Runtime smoke: `AddArrangerAssignments` migration applied on startup; `/Admin/Arrangers` 302→Login anonymously and renders 200 after admin login; dashboard shows the "Manage arrangers" card. Arranger scoping proven by the `Trips_Index_Shows_Self_And_Assigned_Only` integration test.
- **Still open in M1.5:** scoped Managers, reports-to/approver seat, TOTP MFA, cookie/session hardening, traveler-profile depth, Entra group→role mapping / SCIM / prod email confirmation.

### M1.5 auth hardening complete — TOTP MFA + session hardening (2026-07-18)
- Closed the last two M1.5 _must-have_ security gaps. **Cookie/session hardening**
  (`AuthSetup.cs`): idle timeout (`ExpireTimeSpan` + `SlidingExpiration`, default 30 min)
  and a hard absolute cap (default 8h) that survives sliding renewal — `OnSigningIn`
  stamps the first-login instant into the auth properties, `OnValidatePrincipal` rejects
  once it's exceeded and then chains to `SecurityStampValidator.ValidatePrincipalAsync`
  so the M1.5 §2 deactivation kick still fires. Cookie is HttpOnly / SameSite=Lax /
  Secure=Always. Config under `Auth:Session`. Pure `SessionPolicy.IsAbsolutelyExpired`.
- **Optional TOTP MFA** (ASP.NET Core Identity authenticator provider, opt-in per user):
  `/Account/Manage/TwoFactorAuthentication` hub, `EnableAuthenticator` (server-side QR via
  QRCoder 1.6.0 PNG data URI — no JS, works on Linux App Service — plus a typed key),
  `ShowRecoveryCodes` / `GenerateRecoveryCodes` / `Disable2fa`. Login handles
  `RequiresTwoFactor` → `LoginWith2fa` (+ `LoginWithRecoveryCode`). Pure `AuthenticatorUri`
  builds the otpauth:// URI + grouped key.
- **Enforce-for-Admins** (`Auth:Mfa:RequireForAdmins`, default off): `MfaEnforcementMiddleware`
  redirects an authenticated Admin who hasn't enrolled to the setup page (allows `/Account/*`
  + `/healthz`), registered only when the toggle is on.
- **Verification:** `dotnet build -c Release` 0/0; `dotnet test` 107/107 (+10). Runtime
  smoke (dev host): app boots with the new cookie events; anon Manage/2FA + LoginWith2fa
  redirect to Login; admin login → EnableAuthenticator renders a real QR data URI + shared
  key; with `RequireForAdmins=true` an un-enrolled admin hitting `/Trips` is 302'd to
  EnableAuthenticator while an Employee reaches `/Trips` (200) — enforcement is admin-scoped.
- **Still open in M1.5:** scoped Managers, reports-to/approver seat, traveler-profile depth,
  Entra group→role mapping / SCIM / prod email confirmation.

### M1.5 — scoped Managers (2026-07-19)
- Managers were a second global-reach tier alongside Admin; now a Manager is **department-scoped**
  and only Admin has org-wide reach. The whole trip surface already funnelled a "reachable
  traveler ids" set from a thin DB service into the pure `TripAccess.Scope`/`CanAccess`, so the
  change was mostly at the seam: split `CanManageOthers` into `CanManageGlobally` (Admin) +
  `CanSeeTeamRoster` (Manager/Admin, gates "Who's out") + private `HasScopedReach`
  (Manager/Arranger).
- Renamed the DB lookup `ArrangerAccess.DelegatedTravelerIdsAsync` → `TeamAccess.ReachableTravelerIdsAsync`
  and broadened it to union a Manager's department teammates with an Arranger's assignments (a
  user may be both). Non-scoped Employees still never hit the database; a Manager with no
  department reaches nobody (fails closed). `WhoIsOut` now scopes to the manager's department.
- **Verification:** `dotnet build -c Release` 0/0; `dotnet test` 117/117 (updated the now-wrong
  "Manager sees everything" assertions, added scoped-manager unit + integration coverage). Runtime
  smoke (dev seed, logged in as `manager@example.com` / Engineering): `/Trips?When=all` shows only
  Morgan (self) + Evan + Ethan (Engineering), never Ella (Sales) / Emma (Operations) / Fiona
  (Finance); `/Trips/Details/{id}` returns 200 for own + teammates and 404 for out-of-department
  trips; the "Who's out" button still renders for the scoped manager.
- **Still open in M1.5:** reports-to/approver seat, traveler-profile depth, Entra group→role
  mapping / SCIM / prod email confirmation.

### M1.5 — reports-to/approver seat (2026-07-19)
- Seated the org relationship the M6 approval flow will hang off, without building the flow.
  `AppUser` gains a nullable `ApproverId` self-FK (+ `Approver` nav); mapped in `AppDbContext`
  with `DeleteBehavior.Restrict` to match every other `AppUser` FK (no hard-delete path exists,
  and Restrict dodges the SQL Server multiple-cascade-path error). New migration
  `AddUserApprover` (column + `IX_AspNetUsers_ApproverId` + self-FK), snapshot updated.
- Assignment lives on the existing `Admin/Users` Create + Edit pages (approver `<select>` of
  active users; Edit excludes the user themselves). Integrity is a pure, unit-tested
  `Domain/ApproverGraph.CreatesCycle` that walks the approver chain and rejects self-approval
  and any direct/transitive cycle (with a visited-set guard so corrupt data can't loop). The
  Users list shows an Approver column; `ReferenceLists.ApproverSelectListAsync` centralises the
  dropdown. DevSeeder seats a two-level chain (employees → Morgan → Alex) so it's visible in dev.
- Scope: relationship storage + assignment only. Submit/approve/reject, the approver queue, and
  status transitions stay in **M6**, which now just consumes `ApproverId` instead of adding it.
- **Verification (pending local gate):** no .NET SDK in this sandbox, so `dotnet build`/`dotnet test`
  have NOT been run here. New coverage added: `ApproverGraphTests` (self / reciprocal / transitive
  cycle / acyclic / shared-approver / corrupt-loop) and `AdminUserEditTests` (assigns approver,
  rejects self, rejects cycle, clears when blank). Run `dotnet build -c Release` + `dotnet test`
  on a machine with the SDK before merge; apply `AddUserApprover` on deploy.
- **Still open in M1.5:** traveler-profile depth, Entra group→role mapping / SCIM / prod email confirmation.

### M1.5 — traveler profile depth + approver on Details (2026-07-19)
- **Approver on Trip Details:** the trip header now shows the traveler's approver (Details eager-loads
  `Traveler.Approver`). Small duty-of-care win off the `ApproverId` seat.
- **Profile fields on `AppUser`** (migration `AddTravelerProfile`, all optional): passport number/expiry/
  nationality, Known Traveler Number, mobile, emergency contact (name + phone), frequent-flyer numbers,
  seat/meal/hotel prefs, default cost center, `ReimbursementMethod` enum. Default approver reuses the
  existing `ApproverId` rather than a second field.
- **Encryption at rest:** passport number + KTN are the only sensitive identifiers; they're stored as
  Data Protection ciphertext via `Services/SensitiveFieldProtector` (`ISensitiveFieldProtector`, registered
  singleton; `AddDataProtection()` in Program). Columns are suffixed `...Protected`; read/write only
  through the protector. `Unprotect` fails safe to null (never throws) on tampered/lost-key input. Key
  ring note: default file provider under `/home` on App Service is fine for one instance; point at Blob +
  Key Vault for multi-instance/hardened deploys.
- **Editing surfaces (both share one form):** self-service `/Account/Manage/Profile` (new nav link) and
  `Admin/Users/Edit`, via `Models/TravelerProfileInput` (`FromUser`/`ApplyTo` do the encrypt/decrypt +
  trim-to-null) and the `_TravelerProfileFields` partial. Admin `Users/Create` stays minimal (profile
  filled later by the traveler or admin edit).
- **Passport-expiry duty-of-care:** pure `Domain/PassportExpiryStatus` (Unknown/Ok/ExpiringSoon/Expired,
  6-month default threshold) renders a badge in the "Who's out" roster. DevSeeder spreads three expiries
  (expired / expiring-soon / fine) so the states are visible.
- **Verification (pending local gate):** no .NET SDK in this sandbox — `dotnet build`/`dotnet test` NOT run
  here. New coverage: `PassportExpiryStatusTests` (6 boundary cases), `SensitiveFieldProtectorTests`
  (round-trip, empty→null, undecryptable→null, wrong-key→null), `ProfilePageTests` (save encrypts, get
  decrypts, blank clears); `AdminUserEditTests` updated for the new constructor arg. Run `dotnet build -c
  Release` + `dotnet test` on an SDK machine before merge; apply `AddUserApprover` + `AddTravelerProfile`
  on deploy.
- **Still open in M1.5:** Entra group→role mapping / SCIM / prod email confirmation.

### M1.5 — Entra group→role JIT mapping (2026-07-19)
- **What changed:** external (Entra) login no longer leaves roles hand-managed. On every OIDC login the
  user's `groups` claim is reconciled against config `Auth:EntraId:GroupRoleMappings` (array of
  `{GroupId, Role}`, many-to-many) so a promotion/demotion in the directory lands automatically.
- **Design — reconcile, don't overwrite:** pure `Domain/EntraRoleMapper` computes the minimal add/remove
  plan. Only roles that appear as a mapping *target* ("managed" roles) are ever revoked; anything else the
  user holds is left untouched. This is what lets an org govern Manager/Finance via Entra while still
  assigning Admin by hand — a sync can't strip a role Entra doesn't manage. Unknown role names / blank
  group ids in config are dropped (a typo must never silently grant or revoke).
- **Safety:** `Services/EntraRoleSynchronizer` applies the plan via `UserManager`, refuses to remove Admin
  from the last active admin (`AdminGuard`), and calls `UpdateSecurityStampAsync` on change so new roles
  ride the next request. Token-overflow overage claim (`_claim_names`) is detected and logged rather than
  silently granting nothing. Feature is a no-op unless `EnableEntraId` **and** mappings exist, so turning
  Entra on never disturbs existing manual roles; `SyncRolesOnLogin` (default true) is the off switch.
- **Wiring:** injected into `ExternalLogin`. Returning linked user → sync then `RefreshSignInAsync` so the
  change is immediate; fresh provision → sync before `SignInAsync` so the account lands with its roles, not
  just the Employee floor. `groups` claim emission is an Entra app-registration setting (Token
  configuration), noted at the `AuthSetup` OIDC block.
- **Verification:** `dotnet build -c Release` 0 warn / 0 err; `dotnet test` **190/190 green**.
  `EntraRoleMapperTests` (12) covers parse validation, desired-roles from groups (incl. case-insensitive
  group match + multi-group union), reconcile add/revoke, unmanaged-role preservation, empty-mapping no-op.
  `ExternalLogin` itself stays `[ExcludeFromCodeCoverage]` (live-IdP OIDC round-trip; the synchronizer it
  calls is the tested unit + `AdminGuard` already covered).
- **Still open in M1.5:** SCIM auto-provision/deprovision, prod email confirmation.

### M1.5 — Entra app-role pass-through (2026-07-19)
- **Follow-up to the group mapping above.** Added the recommended simple path: set
  `Auth:EntraId:RoleClaimPassthrough=true` and name the Entra **app roles** exactly like ours
  (`Employee`, `Arranger`, `Manager`, `Finance`, `Admin`). The `roles` claim value is then honored
  directly, so config is near-zero (one switch, no GUID mapping table).
- **Model:** pure `EntraRoleMapper.PassthroughPlan(claimValues)` returns `(desired, managed)` where
  desired = claim values matching `Roles.All` (canonical) plus an always-on **Employee floor**, and
  managed = every role except Employee. So Manager/Finance/Admin/Arranger fully mirror the directory
  (assign in Entra → granted next login; unassign → revoked), Employee is never stripped, and unknown
  claim values are ignored. Admin still protected by the last-active-admin guard.
- **Why it beats groups here:** app roles are app-scoped and few, so no token-overflow/overage path;
  values are readable names, not group GUIDs. Pass-through reads the fixed `roles` claim and skips the
  mapping table + overage check. Group mode (`GroupRoleMappings`) still works unchanged for anyone who
  wants GUID→role mapping instead.
- **Verification:** `dotnet build -c Release` 0/0; `dotnet test` **195/195 green** (new `PassthroughPlan`
  cases in `EntraRoleMapperTests`: canonical match, unknown ignored, Employee floor, reconcile revokes an
  unassigned role, case-insensitive).
- **Entra setup:** App registration → **App roles** → create one per app role, value = the role name,
  allowed member types = Users/Groups. Assign users (or groups) in the Enterprise app → Users and groups.
  No Token-configuration groups claim needed in this mode.

### M1.5 — prod email confirmation (2026-07-20)
- **Closed line 94.** Email confirmation is now a config switch: `Auth:RequireConfirmedEmail`
  (default **false**) feeds `SignIn.RequireConfirmedAccount` in `AuthSetup` — was hardcoded off.
  Dev/eval and the four Identity-override tests stay frictionless; prod opts in with one flag.
- **Shared step:** `Services/EmailConfirmationService` (DI-registered in `EmailSetup`) mints the
  Identity token, base64url-encodes it, and sends via the existing `IEmailSender` — same shape as
  the ForgotPassword flow. Link building stays in the caller (Url helper + scheme), so the service
  is HTTP-free; a static `DecodeToken` mirrors the encode.
- **New pages:** `ConfirmEmail` (decode → `ConfirmEmailAsync`, neutral failure state, no 404),
  `RegisterConfirmation` (informational "check your email"), `ResendEmailConfirmation` (neutral,
  no account enumeration — resends only for an **active + unconfirmed** account).
- **Register rework:** self-serve success emails the link and redirects to `RegisterConfirmation`
  **only when** `RequireConfirmedAccount` is on; otherwise the immediate sign-in is unchanged.
  **Invited users skip confirmation** (`EmailConfirmed=true` — the invite already proves ownership)
  and sign straight in, per decision.
- **Login:** added the `IsNotAllowed` branch (correct password, unconfirmed email) → warning +
  resend link, instead of the misleading "invalid email or password". Deactivated accounts are
  still short-circuited earlier by `AppSignInManager`, so `IsNotAllowed` here is the confirmation case.
- **No lockout risk:** seeded admin (`DbInitializer`) and dev users (`DevSeeder`) already set
  `EmailConfirmed=true`, so flipping the flag on never strands an existing account.
- **Should be tested (not run — no SDK in sandbox):** `dotnet build`; confirm-token round-trip
  (`GenerateEmailConfirmationTokenAsync` → base64url → `ConfirmEmail` → `ConfirmEmailAsync` succeeds,
  tampered/expired token fails); with flag **on**, self-serve register → not signed in, redirected to
  `RegisterConfirmation`, login blocked until confirm, then allowed; invite register → signed in
  immediately with no email; login `IsNotAllowed` shows resend link; resend page stays neutral for
  unknown/already-confirmed emails. With flag **off**, register still signs in immediately (existing
  `RegisterTests` behavior unchanged).
- **Still open in M1.5:** SCIM auto-provision/deprovision (line 93) only.

### Note on the pipeline "hello" first
The deployed landing page IS the hello/proving page. Before wiring real features (M1+), do one manual deploy through the pipeline and confirm `https://<app>.azurewebsites.net/healthz` returns `Healthy` to prove the Azure DevOps -> App Service path end to end.

### M3a — expenses core + receipt storage (2026-07-20)
- **First slice of M3.** Scope: expense line items + receipt storage + per-trip rollup.
  Mileage is M3b; multi-currency entry UI, splits, duplicate detection, attendees, and
  the real `ExpensePolicy` are later slices. Design theme honoured: money values are
  **frozen at entry** — `BaseAmount` is stored, never recomputed live.
- **Data:** `Expense` (`Data/Entities/Expense.cs`) + `ExpenseCategory` enum. The
  currency-freeze triplet (`Currency`/`FxRate`/`FxRateDate`/`BaseAmount`) ships now so
  M3c's multi-currency is a UI-only change, not a migration on this hot table — M3a
  writes `Currency=base`, `FxRate=1`, `BaseAmount=Amount`. `IsPersonal` non-reimbursable
  flag, opaque `ReceiptPath`, and `CreatedBy*` audit (arranger-on-behalf) included.
  `AppDbContext`: Expense→Trip **Cascade** (owned, like Destination), Expense→CreatedBy
  **Restrict**, decimal precision `18,2`/`18,6`, `TripId` index. Migration `AddExpenses`
  (hand-authored + snapshot/Designer, since the sandbox has no .NET SDK).
- **Storage:** `IReceiptStorage` behind the existing `Storage:Provider` switch —
  `LocalDiskReceiptStorage` (default; prod path `/home/data/receipts`, same persistence
  rule as the DB) and `AzureBlobReceiptStorage` (private container; adds
  `Azure.Storage.Blobs`). `StorageSetup.AddReceiptStorage` mirrors `EmailSetup`. Opaque
  GUID keys, path-traversal-guarded, allowed types (pdf/jpg/png/heic/webp) + size cap
  (`Storage:MaxReceiptMb`, default 10) via pure `Domain/ReceiptUpload`.
- **Pages** (`/Expenses`, folder-authorized): `ExpensePageModel` base loads the parent
  trip through `TripAccess` so expenses **inherit trip scope** (no new access rules).
  Create/Edit/Delete (receipt upload, replace-deletes-old, delete-removes-blob) and a
  `Receipt` streaming handler **authorized through the trip** — receipts are PII, never a
  public path. Expense list + rollup render as a section on `Trips/Details`.
- **Rollup:** pure `Domain/ExpenseRollup` (category sums of `BaseAmount`, reimbursable vs
  personal split) and `Domain/ExpensePolicyCheck` (minimal advisory over-guideline badge,
  explicitly superseded by M3d). DevSeeder seeds five expenses (incl. one personal + one
  over-guideline meal) on Ethan's completed trip.
- **Verification (pending local gate — no .NET SDK in sandbox):** `dotnet build -c Release`
  + `dotnet test` NOT run here. Should test: `ExpenseRollup` (category sums, personal
  excluded from reimbursable, empty), `ExpensePolicyCheck` (over/under/uncapped),
  `ReceiptUpload` (type/size), local storage save→open→delete round-trip; integration —
  create expense w/ receipt → shows in Details rollup, **receipt stream denied for a user
  outside trip scope** (key auth test), delete removes blob, base-amount freeze
  (`BaseAmount==Amount`, `FxRate==1`). Run `dotnet ef migrations has-pending-model-changes`
  to confirm the hand-authored migration/snapshot match the model; apply `AddExpenses` on
  deploy. Restore adds `Azure.Storage.Blobs`.

### M3b — mileage + effective-dated rate table (2026-07-20)
- **Second slice of M3.** Scope: mileage entries with an effective-dated rate → auto
  amount, ordered waypoints with a round-trip toggle + commute-deduction field, and the
  historical rate table. Design theme honoured: the **rate and derived amount are frozen
  at entry** — resolved from the rate table at the mileage date and stored on the row, so
  editing the rate table never moves an existing entry (same freeze M3a made for FX).
- **Real-world proof the effective-dating matters:** the seeded US IRS business rates
  include the **2026 mid-year jump** (72.5¢/mi from Jan 1 → 76¢/mi from Jul 1, 2026), two
  rows on the same key, so a 30 Jun vs 1 Jul entry freezes different rates.
- **Data:** `MileageRate` (effective-dated: `EffectiveDate`/`Jurisdiction`/`VehicleType`/
  `Unit`/`Rate`, unique on the first four), `MileageEntry` (Trip-owned **Cascade**;
  `Distance`/`Unit`/`IsRoundTrip`/`CommuteDeduction` inputs; **frozen** `Rate`/`Amount` +
  a `Jurisdiction`/`VehicleType` snapshot + nullable `RateId` audit FK **SetNull** so
  losing a rate row never erases history; `CreatedBy*` **Restrict** audit), `MileageWaypoint`
  (entry-owned **Cascade**, `Sequence`+`Label`). Enums `DistanceUnit`, `VehicleType`.
  `AppDbContext` config + precision (18,3 distance / 18,6 rate / 18,2 amount). Migration
  `AddMileage` (hand-authored + Designer/snapshot — no .NET SDK in sandbox).
- **Domain (pure/tested):** `MileageMath` (round-trip ×2 then commute deduction floored at
  0; amount = billable × rate rounded 2dp away-from-zero), `MileageRateResolver` (latest
  `EffectiveDate ≤` date, matching jurisdiction/vehicle/unit case-insensitively),
  `MileageRollup` (miles/km kept separate, frozen amounts summed), `WaypointText`
  (no-JS "one stop per line" ↔ ordered rows).
- **Pages:** `/Mileage` folder (signed-in; `MileagePageModel` **inherits trip scope** via
  `TripAccess`, no new access rules) — Create/Edit/Delete; waypoints via a no-JS textarea;
  Create/Edit **resolve + freeze** the rate and block with a rate-table pointer if none is
  in effect. Mileage section (list + total, frozen rate shown per row) added to
  `Trips/Details`. `/Admin/MileageRates` (RequireAdmin) Index+add+delete of rate rows, plus
  an admin dashboard card. Seed: idempotent US rate table at startup (`MileageRateSeed` via
  `DbInitializer`); DevSeeder adds a round-trip A→B→C entry on Ethan's onsite trip.
- **Verification (pending local gate — no .NET SDK in sandbox):** `dotnet build -c Release`
  + `dotnet test` NOT run here. New unit tests: `MileageMathTests`, `MileageRateResolverTests`
  (incl. the 2026 mid-year boundary), `MileageRollupTests`, `WaypointTextTests`. Should also
  test (integration, on an SDK box): create mileage → appears in Details with the frozen
  rate; entry outside trip scope denied (rides `TripAccess`); **freeze proof** — edit the
  rate table, existing untouched entry's `Amount` is unchanged. Run `dotnet ef migrations
  has-pending-model-changes` to confirm the hand-authored migration/snapshot match the model;
  apply `AddMileage` on deploy.

_(next: M3c — multi-currency entry UI, ExpenseSplit, duplicate detection, attendees/business purpose)_

### M4a — reporting foundation (2026-07-20)
- **First slice of M4.** Scope: the summary report (trips-by-person, cost-by-department,
  cost-by-category, cost-by-cost-center) with a date-range/status/traveler filter. CSV
  export is M4b, charts M4c, `.ics` M4d. Design theme honoured: **read-only aggregation
  over already-frozen numbers** — every figure is a sum of the M3 frozen `BaseAmount` /
  mileage `Amount`, nothing is recomputed. **No migration** (read-only over existing tables).
- **Access (locked this pass):** org-wide reach = **Finance or Admin**; Manager/Arranger get
  a *scoped* view via the existing `TeamAccess` reachable set; plain Employees are off the
  pages (their spend is on `Trips/Details`). New pure `Domain/ReportAccess` (`CanSeeOrgWide`
  → `TripScope.Everyone`, else `TripAccess.Scope`). New `RequireReports` policy
  (Finance/Manager/Arranger/Admin) authorizes the `/Reports` folder; nav link gated to the
  same roles. The M1.5 `RequireFinance` seat finally has a surface.
- **Pure domain (tested):** `ReportFilter` (range parse/validate + `IncludesWindow` overlap),
  `ReportAggregation` (four rollups + grand totals from a flat `ReportRow` list; reimbursable
  excludes personal, distinct-traveler count per dept, unassigned cost-center bucketed last),
  `ReportAccess` (scope decision).
- **Page:** `ReportPageModel` base resolves scope and projects each in-scope, date-filtered,
  non-cancelled trip into a `ReportRow` — expenses (`+Splits`) and mileage are loaded in
  **separate queries keyed by trip id** and grouped in memory, because `Trip` has no inverse
  navigation collections (kept the feature isolated from the entity model). Reuses
  `ExpenseRollup` (so split categories are attributed identically) + `MileageRollup`.
  `/Reports/Index` renders summary cards + four tables with a query-string filter form.
- **Verification:** `dotnet build -c Debug` **0 warn / 0 err**; `dotnet test` **297/297 green**
  (incl. new `ReportFilterTests`, `ReportAggregationTests`, `ReportAccessTests`). Also fixed a
  pre-existing test break unrelated to M4: `RegisterTests` still used the old `RegisterModel`
  ctor (M1.5's email-confirmation change added an `EmailConfirmationService` param that was
  never compiled in the SDK-less sandbox) — added a `file`-scoped no-op `IEmailSender` and passed
  the new arg. Still worth adding as integration coverage on the reports themselves: `/Reports`
  denied for an Employee (403), reachable-only for a Manager vs org-wide for Finance, inverted
  range shows error + empty, cancelled trips excluded. No `ef migrations` step — read-only slice.

### M4b — CSV export (2026-07-20)
- **Second slice of M4.** Scope: CSV export of the four M4a report views. Excel (.xlsx)
  stays deferred — CSV covers "export" with **zero new dependencies**, keeping the
  single-artifact / contributor-friendly ethos.
- **Pure domain (tested):** `Domain/CsvWriter` — RFC-4180 quoting (comma / quote / CR / LF
  wrapped, embedded quotes doubled), CRLF row terminator, UTF-8 **BOM** so Excel opens
  accented text / currency correctly on double-click, and invariant `Money`/`Number`
  formatters so a locale decimal comma never collides with the delimiter.
- **Surface:** `OnGetExportAsync(view)` on `/Reports/Index` reuses the exact same
  scope + filter as the on-screen report (one `LoadRowsAsync` path — export and view can't
  disagree), builds the header + rows per view, and streams a `report-{view}-{yyyyMMdd}.csv`
  `FileResult`. Per-table "Export CSV" links carry the live filter via `asp-all-route-data`;
  an invalid (inverted) range redirects back to the page so the error shows there.
- **Verification:** `dotnet build -c Debug` **0 warn / 0 err**; `dotnet test` **297/297 green**
  (incl. new `CsvWriterTests` — quoting matrix, quote doubling, newline fields, BOM prefix,
  invariant money). Two fixes surfaced by the real compile (warnings-as-errors): the Razor
  `asp-all-route-data` attribute needed single quotes around the `ExportRoute("…")` call, and
  the export filename's `DateOnly.ToString` needed `CultureInfo.InvariantCulture` (CA1305).
  Still worth a manual smoke test: each Export link downloads a CSV whose rows match the
  on-screen table for the same filter, and a scoped Manager's export excludes out-of-scope travelers.

### M4c — dashboard charts (2026-07-20)
- **Third slice of M4.** Scope: three Chart.js visuals on the reports view. Placed on the
  scoped `/Reports/Index` (not the public home dashboard) — spend charts must inherit the
  `RequireReports` gate + `ReportAccess` scope so Employees never see org spend.
- **Data:** new pure `ReportAggregation.ByMonth` (`MonthReportLine`, keyed to the trip's
  start month, ordered chronologically so the timeline reads left-to-right) added to
  `ReportSummary`. Charts and tables share one aggregation, so they can't drift. The page
  serializes category/department/month series (money as plain numbers, month labels
  pre-formatted invariantly) into a `<script type="application/json">` blob; `ChartDataJson`
  is `"null"` when there's nothing to plot.
- **Client:** Chart.js 4.4.1 from cdnjs in `@@section Scripts` (first CDN use; no CSP header
  in the app, so inline init is fine — no JS build step, honouring the server-rendered ethos).
  Vanilla init parses the JSON and draws a bar (month), doughnut (category), bar (department),
  formatting money in tooltips/axes. Guards for missing Chart global, bad JSON, or empty series.
- **Verification:** `dotnet build -c Debug` **0 warn / 0 err**; `dotnet test` **298/298 green**
  (new `By_Month_Groups_On_Trip_Start_And_Sorts_Chronologically`). Worth a manual browser
  smoke: charts render for a Finance user, match the table numbers, and a scoped Manager's
  charts show only their department's spend. Chart.js is CDN-loaded, so an offline/blocked
  CDN degrades to tables-only (init bails) rather than erroring.

### M4d — itinerary .ics export (2026-07-20)
- **Final slice of M4.** Scope: download a trip's itinerary as an `.ics` calendar file.
- **Pure domain (tested):** `Domain/IcsBuilder` emits an RFC 5545 VCALENDAR with one all-day
  VEVENT per leg. Legs are date-only, so `DTSTART;VALUE=DATE` with an **exclusive** `DTEND`
  (depart + 1 day) so the event visually covers the depart day. Each event carries a **stable
  UID** (`trip-{id}-leg-{legId}@traveltracker.local`) so re-importing updates rather than
  duplicates. Summary = `{Purpose}: {City}`; location joins city/state/country; description
  gathers transport, lodging, confirmation #, notes. TEXT values are escaped (`\ ; , newline`)
  and long lines folded at 75 octets per spec. A trip with no legs falls back to a single
  event spanning the trip window.
- **Surface:** `OnGetIcsAsync` handler on `Trips/Details` streams `text/calendar`
  (`trip-{id}.ics`), authorized through `LoadAuthorizedTripAsync` so an out-of-scope trip
  404s (never leaks) — same access seam as the page and the receipt stream. "Add to calendar"
  button added to the Details header. Per-trip download only; a subscribable per-user feed
  stays deferred (adds a token/auth surface).
- **Verification:** `dotnet build -c Debug` **0 warn / 0 err**; `dotnet test` **305/305 green**
  (new `IcsBuilderTests`: headers, one-VEVENT-per-leg + stable UID, exclusive DTEND, location
  join, TEXT escaping, legless fallback, UTC DTSTAMP). Worth a manual smoke: download from a
  trip and import into Google/Outlook/Apple Calendar to confirm the all-day events land on the
  right days. No migration — read-only slice.

**M4 (Reporting + exports) complete.** All four checklist items shipped across a–d: reports
(trips-by-person / cost-by-department / category / cost-center with date-range + scope), CSV
export, dashboard charts, and itinerary `.ics`. No migrations in the whole milestone — every
slice is read-only over the M2/M3 tables. Excel (.xlsx) intentionally deferred (CSV covers it).
