using System.Globalization;
using TravelTracker.Web.Data.Entities;

namespace TravelTracker.Web.Services.Email;

// Which {{Token}} placeholders each template may use, which are mandatory, and
// sample values for the admin preview / send-test (ADR-0005). Pure data.
public static class EmailTemplateTokens
{
    // Available in every template. RecipientName is empty when the recipient has
    // no account yet (invitations), like any other unset optional token.
    public const string AppName = "AppName";
    public const string RecipientName = "RecipientName";
    public const string RecipientEmail = "RecipientEmail";

    // Derived: rendered as <p>Comment: ...</p> when Comment is non-empty, else
    // nothing. Covers the only conditional without adding conditional syntax.
    public const string Comment = "Comment";
    public const string CommentBlock = "CommentBlock";

    public static readonly IReadOnlyList<string> Common = new[] { AppName, RecipientName, RecipientEmail };

    private static readonly IReadOnlyList<string> TripTokens =
        new[] { "Trip.Code", "Trip.Purpose", "Trip.Dates" };

    public sealed record Spec(
        string Title,
        string Description,
        IReadOnlyList<string> Allowed,
        IReadOnlyList<string> Required);

    private static readonly IReadOnlyDictionary<EmailTemplateKey, Spec> Specs =
        new Dictionary<EmailTemplateKey, Spec>
        {
            [EmailTemplateKey.PasswordReset] = new(
                "Password reset",
                "Sent when someone requests a reset link. Security email: always on.",
                Common.Concat(new[] { "ResetLink" }).ToList(),
                new[] { "ResetLink" }),

            [EmailTemplateKey.EmailConfirmation] = new(
                "Email confirmation",
                "Sent after self-serve registration and from the resend page. Security email: always on.",
                Common.Concat(new[] { "ConfirmLink" }).ToList(),
                new[] { "ConfirmLink" }),

            [EmailTemplateKey.TripSubmitted] = new(
                "Trip submitted for approval",
                "Sent to the approver when a traveler submits a trip.",
                Common.Concat(TripTokens).Concat(new[] { "Traveler.Name", "ApprovalLink" }).ToList(),
                new[] { "ApprovalLink" }),

            [EmailTemplateKey.TripApproved] = new(
                "Trip approved",
                "Sent to the traveler when their trip is approved.",
                Common.Concat(TripTokens).Concat(new[] { "Approver.Name", Comment, CommentBlock, "TripLink" }).ToList(),
                Array.Empty<string>()),

            [EmailTemplateKey.TripRejected] = new(
                "Trip rejected",
                "Sent to the traveler when their trip is rejected (a comment is always present).",
                Common.Concat(TripTokens).Concat(new[] { "Approver.Name", Comment, CommentBlock, "TripLink" }).ToList(),
                Array.Empty<string>()),

            [EmailTemplateKey.Invitation] = new(
                "Invitation",
                "Sent when an admin creates or resends an invite. The invitee has no account yet, so RecipientName is empty.",
                Common.Concat(new[] { "InviteLink", "Invite.Role", "Invite.ExpiresAt", "InvitedBy.Name" }).ToList(),
                new[] { "InviteLink" }),
        };

    public static Spec For(EmailTemplateKey key) => Specs[key];

    public static IEnumerable<EmailTemplateKey> Keys => Specs.Keys;

    // Trip window, formatted the same way the hardcoded emails did it.
    public static string TripDates(DateOnly start, DateOnly end) =>
        $"{start:MMM d} – {end:MMM d, yyyy}";

    // Invite expiry, formatted like the Invites list (date only).
    public static string InviteExpiry(DateTimeOffset expiresAt) => expiresAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    // Realistic stand-in values for the admin preview and "send test to me".
    public static IReadOnlyDictionary<string, string?> Sample(EmailTemplateKey key)
    {
        var d = new Dictionary<string, string?>
        {
            [RecipientName] = key == EmailTemplateKey.Invitation ? "" : "Sam Traveler",
            [RecipientEmail] = "sam@example.com",
        };
        switch (key)
        {
            case EmailTemplateKey.PasswordReset:
                d["ResetLink"] = "https://example.com/Account/ResetPassword?email=sam%40example.com&token=SAMPLE";
                break;
            case EmailTemplateKey.EmailConfirmation:
                d["ConfirmLink"] = "https://example.com/Account/ConfirmEmail?userId=SAMPLE&token=SAMPLE";
                break;
            case EmailTemplateKey.TripSubmitted:
                AddTrip(d);
                d["Traveler.Name"] = "Sam Traveler";
                d["ApprovalLink"] = "https://example.com/Trips/Details/1";
                break;
            case EmailTemplateKey.TripApproved:
                AddTrip(d);
                d["Approver.Name"] = "Alex Approver";
                d[Comment] = "Approved — keep receipts for the hotel.";
                d["TripLink"] = "https://example.com/Trips/Details/1";
                break;
            case EmailTemplateKey.TripRejected:
                AddTrip(d);
                d["Approver.Name"] = "Alex Approver";
                d[Comment] = "Please move the trip a week later to line up with the client visit.";
                d["TripLink"] = "https://example.com/Trips/Details/1";
                break;
            case EmailTemplateKey.Invitation:
                d["InviteLink"] = "https://example.com/Account/Register?token=SAMPLE";
                d["Invite.Role"] = "Employee";
                d["Invite.ExpiresAt"] = InviteExpiry(DateTimeOffset.UtcNow.AddDays(7));
                d["InvitedBy.Name"] = "Alex Admin";
                break;
        }
        return d;

        static void AddTrip(Dictionary<string, string?> d)
        {
            d["Trip.Code"] = "TT-2026-7K4-Q9M";
            d["Trip.Purpose"] = "Client kickoff in Denver";
            d["Trip.Dates"] = TripDates(new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 8));
        }
    }
}
