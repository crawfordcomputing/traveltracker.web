using TravelTracker.Web.Data.Entities;
using TravelTracker.Web.Services.Email;
using Xunit;

namespace TravelTracker.Tests;

// ADR-0005 pure renderer: token encoding, CommentBlock, subject hardening,
// save-time validation, sanitizing, and default output parity with the
// hardcoded strings that existed before templates.
public class EmailTemplateRendererTests
{
    private static Dictionary<string, string?> Tokens(params (string K, string? V)[] kv) =>
        kv.ToDictionary(p => p.K, p => p.V);

    private static EmailTemplateContent Render(EmailTemplateKey key, EmailTemplateContent t,
        params (string K, string? V)[] kv) =>
        EmailTemplateRenderer.Render(key, t, Tokens(kv));

    // ---- Encoding --------------------------------------------------------------

    [Fact]
    public void Body_Token_Values_Are_Html_Encoded()
    {
        var t = new EmailTemplateContent("s", "<p>{{Trip.Purpose}}</p><a href=\"{{ApprovalLink}}\">go</a>");
        var r = Render(EmailTemplateKey.TripSubmitted, t,
            ("Trip.Purpose", "<script>alert(1)</script>"),
            ("ApprovalLink", "https://x/a?b=1&c=\"2\""));

        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", r.HtmlBody);
        Assert.DoesNotContain("<script>", r.HtmlBody);
        Assert.Contains("href=\"https://x/a?b=1&amp;c=&quot;2&quot;\"", r.HtmlBody);
    }

    [Fact]
    public void Subject_Gets_Plain_Text_With_Line_Breaks_Stripped()
    {
        var t = new EmailTemplateContent("Trip: {{Trip.Purpose}}", "<a href=\"{{ApprovalLink}}\">x</a>");
        var r = Render(EmailTemplateKey.TripSubmitted, t,
            ("Trip.Purpose", "A & B\r\nBcc: evil@x.io"), ("ApprovalLink", "https://x"));

        Assert.Equal("Trip: A & BBcc: evil@x.io", r.Subject); // not encoded, single line
    }

    [Fact]
    public void Missing_Optional_Token_Renders_Empty()
    {
        var t = new EmailTemplateContent("Hi {{RecipientName}}", "<p>[{{RecipientName}}]</p><a href=\"{{InviteLink}}\">x</a>");
        var r = Render(EmailTemplateKey.Invitation, t, ("InviteLink", "https://x"));
        Assert.Equal("Hi ", r.Subject);
        Assert.Contains("<p>[]</p>", r.HtmlBody);
    }

    // ---- CommentBlock ----------------------------------------------------------

    [Fact]
    public void CommentBlock_Renders_Encoded_Paragraph_When_Present()
    {
        var t = new EmailTemplateContent("s", "<p>x</p>{{CommentBlock}}");
        var r = Render(EmailTemplateKey.TripRejected, t, ("Comment", "Too <b>pricey</b>"));
        Assert.Equal("<p>x</p><p>Comment: Too &lt;b&gt;pricey&lt;/b&gt;</p>", r.HtmlBody);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CommentBlock_Is_Empty_Without_A_Comment(string? comment)
    {
        var t = new EmailTemplateContent("s", "<p>x</p>{{CommentBlock}}");
        Assert.Equal("<p>x</p>", Render(EmailTemplateKey.TripApproved, t, ("Comment", comment)).HtmlBody);
    }

    // ---- Render-time guards (drifted stored templates) --------------------------

    [Fact]
    public void Render_Throws_On_Unknown_Token()
    {
        var t = new EmailTemplateContent("s", "<a href=\"{{ResetLink}}\">x</a>{{Trip.Code}}");
        Assert.Throws<EmailTemplateException>(() =>
            Render(EmailTemplateKey.PasswordReset, t, ("ResetLink", "https://x")));
    }

    [Fact]
    public void Render_Throws_When_Required_Token_Unused_Or_Empty()
    {
        var noLink = new EmailTemplateContent("s", "<p>no link</p>");
        Assert.Throws<EmailTemplateException>(() =>
            Render(EmailTemplateKey.PasswordReset, noLink, ("ResetLink", "https://x")));

        var ok = EmailTemplateDefaults.Get(EmailTemplateKey.PasswordReset);
        Assert.Throws<EmailTemplateException>(() =>
            Render(EmailTemplateKey.PasswordReset, ok, ("ResetLink", "")));
    }

    // ---- Save-time validation --------------------------------------------------

    [Fact]
    public void Validate_Rejects_Unknown_And_Missing_Required_Tokens()
    {
        var errors = EmailTemplateRenderer.Validate(EmailTemplateKey.Invitation,
            "Welcome {{Trip.Code}}", "<p>hello {{ Bogus }}</p>");

        Assert.Contains("Unknown token {{Trip.Code}}.", errors);
        Assert.Contains("Unknown token {{Bogus}}.", errors);
        Assert.Contains("Required token {{InviteLink}} is missing.", errors);
    }

    [Fact]
    public void Validate_Accepts_Required_Token_In_Subject_Or_Body()
    {
        Assert.Empty(EmailTemplateRenderer.Validate(EmailTemplateKey.PasswordReset,
            "Reset", "<a href=\"{{ResetLink}}\">Reset</a>"));
    }

    [Fact]
    public void Validate_Enforces_Size_Limits()
    {
        var longSubject = new string('s', EmailTemplateRenderer.MaxSubjectLength + 1);
        var longBody = "{{ResetLink}}" + new string('b', EmailTemplateRenderer.MaxBodyLength);

        var errors = EmailTemplateRenderer.Validate(EmailTemplateKey.PasswordReset, longSubject, longBody);

        Assert.Contains(errors, e => e.StartsWith("Subject must be", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.StartsWith("Body must be", StringComparison.Ordinal));
    }

    [Fact]
    public void Every_Default_Passes_Its_Own_Validation()
    {
        foreach (var key in EmailTemplateTokens.Keys)
        {
            var d = EmailTemplateDefaults.Get(key);
            Assert.Empty(EmailTemplateRenderer.Validate(key, d.Subject, d.HtmlBody));
        }
    }

    // ---- Sanitizer --------------------------------------------------------------

    [Fact]
    public void Sanitizer_Strips_Script_Handlers_And_Js_Urls_But_Keeps_Tokens()
    {
        var dirty = "<p onclick=\"steal()\">Hi {{RecipientName}}</p>" +
                    "<script>alert(1)</script><iframe src=\"https://x\"></iframe>" +
                    "<a href=\"javascript:alert(1)\">bad</a>" +
                    "<a href=\"{{ResetLink}}\">Reset</a>";

        var clean = EmailTemplateSanitizer.Sanitize(dirty);

        Assert.DoesNotContain("onclick", clean);
        Assert.DoesNotContain("<script", clean);
        Assert.DoesNotContain("<iframe", clean);
        Assert.DoesNotContain("javascript:", clean);
        Assert.Contains("{{RecipientName}}", clean);
        Assert.Contains("href=\"{{ResetLink}}\"", clean);
    }

    // ---- Defaults match the pre-template hardcoded output -----------------------
    // Expected strings are the old call-site interpolations evaluated with the same
    // inputs (links without '&', which the old code didn't encode and the new code
    // correctly writes as &amp;).

    private const string AppName = "Travel Tracker";

    [Fact]
    public void Default_PasswordReset_Matches_Previous_Output()
    {
        var r = Render(EmailTemplateKey.PasswordReset, EmailTemplateDefaults.Get(EmailTemplateKey.PasswordReset),
            ("AppName", AppName), ("ResetLink", "https://app/Account/ResetPassword?token=T"));

        Assert.Equal("Reset your Travel Tracker password", r.Subject);
        Assert.Equal(
            "<p>Someone requested a password reset for your account.</p>" +
            "<p><a href=\"https://app/Account/ResetPassword?token=T\">Reset your password</a></p>" +
            "<p>If this wasn't you, you can safely ignore this email.</p>", r.HtmlBody);
    }

    [Fact]
    public void Default_EmailConfirmation_Matches_Previous_Output()
    {
        var r = Render(EmailTemplateKey.EmailConfirmation, EmailTemplateDefaults.Get(EmailTemplateKey.EmailConfirmation),
            ("AppName", AppName), ("ConfirmLink", "https://app/Account/ConfirmEmail?token=T"));

        Assert.Equal("Confirm your Travel Tracker email", r.Subject);
        Assert.Equal(
            "<p>Welcome to Travel Tracker. Please confirm your email address to finish setting up your account.</p>" +
            "<p><a href=\"https://app/Account/ConfirmEmail?token=T\">Confirm your email</a></p>" +
            "<p>If you didn't create this account, you can safely ignore this email.</p>", r.HtmlBody);
    }

    private static readonly DateOnly Start = new(2026, 10, 5);
    private static readonly DateOnly End = new(2026, 10, 8);

    [Fact]
    public void Default_TripSubmitted_Matches_Previous_Output()
    {
        var r = Render(EmailTemplateKey.TripSubmitted, EmailTemplateDefaults.Get(EmailTemplateKey.TripSubmitted),
            ("Traveler.Name", "Sam O'Neil"), ("Trip.Purpose", "Client <kickoff>"),
            ("Trip.Dates", EmailTemplateTokens.TripDates(Start, End)), ("Trip.Code", "TT-2026-7K4-Q9M"),
            ("ApprovalLink", "https://app/Trips/Details/1"));

        Assert.Equal("Trip awaiting your approval: Client <kickoff>", r.Subject);
        Assert.Equal(
            "<p>Sam O&#x27;Neil submitted a trip for your approval.</p>" +
            "<p><strong>Client &lt;kickoff&gt;</strong><br>" +
            $"{Start:MMM d} – {End:MMM d, yyyy}<br>" +
            "Trip code: TT-2026-7K4-Q9M</p>" +
            "<p><a href=\"https://app/Trips/Details/1\">Review and approve or reject this trip</a></p>", r.HtmlBody);
    }

    [Theory]
    [InlineData(EmailTemplateKey.TripApproved, "approved", null)]
    [InlineData(EmailTemplateKey.TripRejected, "rejected", "Move it a week")]
    public void Default_TripDecision_Matches_Previous_Output(EmailTemplateKey key, string verb, string? comment)
    {
        var r = Render(key, EmailTemplateDefaults.Get(key),
            ("Trip.Purpose", "Denver"), ("Trip.Dates", EmailTemplateTokens.TripDates(Start, End)),
            ("Trip.Code", "TT-2026-7K4-Q9M"), ("Approver.Name", "Alex"), ("Comment", comment),
            ("TripLink", "https://app/Trips/Details/1"));

        Assert.Equal($"Your trip was {verb}: Denver", r.Subject);
        Assert.Equal(
            $"<p>Your trip <strong>Denver</strong> ({Start:MMM d} – {End:MMM d, yyyy}) " +
            $"was {verb} by Alex.</p>" +
            "<p>Trip code: TT-2026-7K4-Q9M</p>" +
            (comment is null ? "" : $"<p>Comment: {comment}</p>"), r.HtmlBody);
    }

    [Fact]
    public void Default_Invitation_Includes_Link_And_Details()
    {
        var r = Render(EmailTemplateKey.Invitation, EmailTemplateDefaults.Get(EmailTemplateKey.Invitation),
            ("AppName", AppName), ("RecipientEmail", "guest@contractor.io"),
            ("InviteLink", "https://app/Account/Register?token=T"), ("Invite.Role", "Employee"),
            ("Invite.ExpiresAt", "2026-09-18"), ("InvitedBy.Name", "Alex Admin"));

        Assert.Equal("You're invited to Travel Tracker", r.Subject);
        Assert.Contains("href=\"https://app/Account/Register?token=T\"", r.HtmlBody);
        Assert.Contains("Alex Admin has invited guest@contractor.io to join Travel Tracker as Employee.", r.HtmlBody);
        Assert.Contains("2026-09-18", r.HtmlBody);
    }
}
