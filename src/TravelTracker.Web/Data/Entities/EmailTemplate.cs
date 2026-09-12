using System.ComponentModel.DataAnnotations;

namespace TravelTracker.Web.Data.Entities;

// One value per outbound email the app sends (ADR-0005). Approved and Rejected
// are separate keys so admins can word a rejection differently from an approval.
public enum EmailTemplateKey
{
    PasswordReset = 0,
    EmailConfirmation = 1,
    TripSubmitted = 2,
    TripApproved = 3,
    TripRejected = 4,
    Invitation = 5,

    // Sent when an admin creates a user: a one-time link to set their own password.
    // Separate from PasswordReset so the wording can welcome a brand-new account.
    AccountSetup = 6,
}

// Who a template variant is for. Internal = recipient's domain is one of ours
// (Email:InternalDomains, falling back to Auth:Registration:AllowedDomains);
// External = any other domain; Any = applies when no audience-specific row exists
// or the recipient can't be classified (no domains configured).
public enum EmailAudience
{
    Any = 0,
    Internal = 1,
    External = 2,
}

// An admin override for one (Key, Audience) pair. Rows exist ONLY for overrides:
// the built-in defaults live in code (Services/Email/EmailTemplateDefaults) and
// are never seeded, so a release that improves a default reaches every install
// that hasn't customized it. "Reset to default" is simply deleting the row.
public class EmailTemplate
{
    public int Id { get; set; }

    public EmailTemplateKey Key { get; set; }
    public EmailAudience Audience { get; set; }

    [StringLength(256)]
    public string Subject { get; set; } = string.Empty;

    // Admin-authored HTML with {{Token}} placeholders. Sanitized on save
    // (HtmlSanitizer) and capped at 20,000 chars by validation, not the column.
    public string HtmlBody { get; set; } = string.Empty;

    public DateTimeOffset UpdatedAt { get; set; }

    public string UpdatedById { get; set; } = string.Empty;
    public AppUser? UpdatedBy { get; set; }
}
