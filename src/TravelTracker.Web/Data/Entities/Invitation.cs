namespace TravelTracker.Web.Data.Entities;

// A pending invite to create one local account. Used when
// Auth:Registration:Mode = Invite. The raw token is shown to the admin exactly
// once (as a link) and never stored; only its SHA-256 hash lives here, so a DB
// leak can't be replayed into account creation.
public class Invitation
{
    public int Id { get; set; }

    // The address this invite is bound to. Registration must use this exact email.
    public string Email { get; set; } = string.Empty;

    // Role and department the new account is granted — chosen by the admin, not
    // the registrant, so invite mode can't be used to self-assign Admin.
    public string Role { get; set; } = Roles.Employee;
    public int? DepartmentId { get; set; }
    public Department? Department { get; set; }

    // SHA-256 (hex) of the raw token. Never store the token itself.
    public string TokenHash { get; set; } = string.Empty;

    public string InvitedByUserId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }

    // Set when the invite is redeemed; a non-null value means it's spent.
    public DateTimeOffset? AcceptedAt { get; set; }

    public bool IsRedeemable(DateTimeOffset now) =>
        AcceptedAt is null && now < ExpiresAt;
}
