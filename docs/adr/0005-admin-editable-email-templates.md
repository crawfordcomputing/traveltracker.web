# ADR-0005: Admin-Editable Email Templates (Internal / External Variants)

**Status:** Proposed
**Date:** 2026-09-11
**Deciders:** Project maintainer(s)

## Context

Every outbound email is a hardcoded C# string. Changing wording, adding a
company signature, or pointing people to a help desk means a code change and a
deploy. Admins also want different wording for **internal** recipients
(employees on the company domain) and **external** recipients (contractors,
guests, consultants on other domains).

Confirmed from the current code, there are four emails at three call sites:

| # | Email | Sent from | Recipient | Failure handling |
|---|-------|-----------|-----------|------------------|
| 1 | Password reset | `Account/ForgotPassword.OnPostAsync` | the user | propagates |
| 2 | Email confirmation | `Services/EmailConfirmationService.SendLinkAsync` | the user | propagates |
| 3 | Trip submitted for approval | `Trips/Details.OnPostSubmitAsync` | effective approver | swallowed (`TrySendAsync`) |
| 4 | Trip approved / rejected | `Trips/Details.DecideAsync` | traveler | swallowed (`TrySendAsync`) |

Invitations do **not** send email today (the admin copies the link).

The sending pipeline is already cleanly layered:
`caller -> IEmailSender (AuditingEmailSender -> Logging|Smtp)`.
`AuditingEmailSender` writes a `NotificationLog` row per attempt and deliberately
never stores bodies, because bodies carry one-time reset/confirmation links.

## Decision

Add a template layer **above** `IEmailSender`. The sender, the auditing
decorator, and the SMTP/Log provider switch stay unchanged.

### Template keys

An enum `EmailTemplateKey`, one value per email:

- `PasswordReset`
- `EmailConfirmation`
- `TripSubmitted`
- `TripApproved`
- `TripRejected`

Approved and rejected are split (today they share one string with a `{verb}`)
so admins can word a rejection differently from an approval.

### Audience: internal vs external

- Enum `EmailAudience { Any = 0, Internal = 1, External = 2 }`.
- A pure `Domain/RecipientClassifier` decides per recipient at send time:
  - Domain (after `@`, case-insensitive) is in `Email:InternalDomains` ->
    `Internal`.
  - `Email:InternalDomains` not set -> fall back to
    `Auth:Registration:AllowedDomains` (already the list of "our" domains).
  - Neither list set -> classification is unknown -> `Any`.
  - Otherwise -> `External`.
- Exact domain match only. Subdomains must be listed explicitly
  (`corp.example.com`). Avoids surprising matches like `example.com.evil.io`.

### Resolution order (per send)

1. DB row for `(Key, Audience)` where Audience is the classified audience.
2. DB row for `(Key, Any)`.
3. Built-in default in code.

So an admin can customize just the external version and leave internal on the
default, or set one `Any` override for everyone.

### Storage

New entity `EmailTemplate`:

| Column | Type | Notes |
|--------|------|-------|
| `Id` | int | PK |
| `Key` | `EmailTemplateKey` | |
| `Audience` | `EmailAudience` | unique index on `(Key, Audience)` |
| `Subject` | nvarchar(256) | plain text |
| `HtmlBody` | nvarchar(max) | validated max 20,000 chars |
| `UpdatedAt` | datetimeoffset | |
| `UpdatedById` | string FK -> AppUser | `Restrict`, matching other audit FKs |

**Defaults are not seeded.** Rows exist only for overrides. Defaults live in
code (`Services/Email/EmailTemplateDefaults.cs`, moved verbatim from today's
strings). This differs from ADR-0003 (which seeded caps via `HasData`) on
purpose: when a release improves a default email, every install that has not
customized it gets the improvement automatically, and "Reset to default" is just
deleting the row.

### Placeholders

- Syntax `{{Token}}`. Plain token replacement only: no loops, no conditionals,
  no expressions. No template engine dependency.
- Each key declares its allowed tokens and which are **required**:

| Key | Tokens | Required |
|-----|--------|----------|
| All | `{{AppName}}`, `{{RecipientName}}` | |
| `PasswordReset` | `{{ResetLink}}` | `{{ResetLink}}` |
| `EmailConfirmation` | `{{ConfirmLink}}` | `{{ConfirmLink}}` |
| `TripSubmitted` | `{{Trip.Code}}`, `{{Trip.Purpose}}`, `{{Trip.Dates}}`, `{{Traveler.Name}}`, `{{ApprovalLink}}` | `{{ApprovalLink}}` |
| `TripApproved` / `TripRejected` | `{{Trip.Code}}`, `{{Trip.Purpose}}`, `{{Trip.Dates}}`, `{{Approver.Name}}`, `{{Comment}}`, `{{CommentBlock}}`, `{{TripLink}}` | |

- `{{CommentBlock}}` renders `<p>Comment: ...</p>` when a comment exists and
  nothing otherwise. That covers today's only conditional without adding
  conditional syntax.
- `{{Trip.Code}}` depends on ADR-0004.

### Rendering and safety

A pure static `EmailTemplateRenderer`:

- **Body:** every token value is HTML-encoded before insertion (links are
  attribute-encoded). Admin HTML is kept; token values can never inject markup.
- **Subject:** tokens inserted as plain text; CR/LF stripped to block header
  injection.
- **On save**, validation rejects: unknown tokens, missing required tokens,
  subject over 256 chars, body over 20,000 chars.
- **On save**, the body is sanitized with `HtmlSanitizer` (Ganss.Xss, MIT):
  strips `<script>`, `<iframe>`, `on*` handlers, and `javascript:` URLs. Mail
  clients mostly block these anyway; the real reason is the admin preview.
- **Preview** renders in `<iframe sandbox srcdoc="...">` so a malicious or
  broken template cannot run script in the admin session.
- **Runtime fallback:** if a stored template fails to render (should not happen
  after validation, but data can drift across versions), log a warning and send
  the built-in default. A bad template must never block a password reset.

### Service

```csharp
public interface IEmailTemplateService
{
    Task SendAsync(EmailTemplateKey key, string recipient,
                   IReadOnlyDictionary<string, string?> tokens);
}
```

Classifies -> resolves -> renders -> calls `IEmailSender.SendAsync`. The three
call sites switch from building HTML to passing tokens. Their failure handling
does not change (propagate vs `TrySendAsync`).

### Audit

- `NotificationLog` gains nullable `TemplateKey` and `Audience` columns so the
  Notifications page can show which template was used. Still never stores the
  body.
- `EmailTemplate.UpdatedAt/UpdatedById` record the last editor. Full version
  history is out of scope.

### Admin UI

Under `/Admin/EmailTemplates` (inherits the `RequireAdmin` folder policy). New
card on the Admin home.

- **List:** one row per key with a short description and three status chips:
  Any / Internal / External, each "Default" or "Customized".
- **Edit** `(key, audience)`: subject input, HTML body textarea, token reference
  panel (click to insert), live preview with sample data, validation messages.
- **Send test to me:** renders with sample data and sends to the signed-in
  admin through the normal pipeline (so it shows in Notifications).
- **Reset to default:** deletes the row, with a confirm step.
- The edit page shows which audience a sample address would classify as, so
  admins can sanity-check `Email:InternalDomains`.

### Configuration

| App setting | Default | Notes |
|-------------|---------|-------|
| `Email__InternalDomains__{n}` | (none) | Indexed array. Falls back to `Auth__Registration__AllowedDomains`. |
| `Email__AppName` | `Travel Tracker` | Value for `{{AppName}}`. |

Add both to the README's Email settings table.

## Alternatives considered

| Option | Why not |
|--------|---------|
| Template engine (Scriban / Fluid) | Loops and conditionals are not needed for five short emails. Adds a dependency and a sandboxing surface. Revisit if templates grow. |
| Razor views as templates | Needs a recompile or file edits on the server; not admin-editable. |
| Seed defaults into the DB | Every install freezes today's wording; improved defaults never reach them. |
| Classify audience by user role or an `IsExternal` flag | More precise, but the recipient of reset/confirmation may not have a finished profile, and there is no such flag today. Domain match works for every email with zero new user data. Could be added later as an override. |
| Separate From address per audience | Plausible need, but it's a sender/deliverability concern (SPF/DKIM per domain). Kept as a follow-up. |

## Consequences

**Positive**
- Admins change wording, branding, and help text with no deploy.
- Internal and external recipients can get different messaging.
- Required-token validation makes it hard to ship a reset email with no link.
- Sending, auditing, and provider switching are untouched.

**Negative / limits**
- New dependency: `HtmlSanitizer` (MIT).
- Admin-authored HTML can still look bad in some mail clients. Preview and
  send-test mitigate but can't guarantee rendering.
- No plain-text alternative part yet (SMTP sends HTML only, same as today).
- Single language. No localization.
- No template history or rollback beyond "reset to default."

## Test plan

- `RecipientClassifier`: listed domain, unlisted domain, fallback to
  registration domains, no config -> Any, case-insensitivity, lookalike domains.
- `EmailTemplateRenderer`: token encoding (HTML and attribute), `CommentBlock`
  present/absent, CR/LF stripped from subject, unknown token rejected, missing
  required token rejected, size limits.
- Resolution order: (Key, Audience) beats (Key, Any) beats default.
- Runtime fallback: corrupt stored template -> default sent, warning logged.
- Each of the five emails renders the same as today when no overrides exist
  (snapshot the default output before refactoring).
- Admin pages: non-admin gets 403; save, preview, reset, send-test.
- `NotificationLog` records `TemplateKey`/`Audience`, and still no body.

## Follow-ups (not part of this change)

- Per-audience From address / reply-to.
- Plain-text alternative part (auto-generated from HTML).
- Invitation email (today admins copy the link) using this same system.
- Template version history with diff and rollback.
- Per-user `IsExternal` override when domain classification is wrong.
- Localization.

## Open questions

1. Is domain the right internal/external signal for you, or do you have
   internal staff on multiple domains (need several entries) or externals on
   your domain (need the per-user override sooner)?
   Yes, domain is fine, we can use the 
2. Should admins be allowed to turn off a non-security email (e.g. stop
   `TripApproved` emails) or only edit them? Security emails stay always-on.
   Leave them always on, no ability to turn off
3. Is a plain textarea acceptable for v1, or do admins need a WYSIWYG editor?
  I think plain text area is ok for V1
