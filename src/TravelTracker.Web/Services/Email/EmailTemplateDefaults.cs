using TravelTracker.Web.Data.Entities;

namespace TravelTracker.Web.Services.Email;

// Subject + HTML body of a template, either a built-in default or a stored override.
public sealed record EmailTemplateContent(string Subject, string HtmlBody);

// Built-in wording for every email (ADR-0005). These are the strings that used to
// live at each call site, moved verbatim with tokens in place of interpolation.
// They are the fallback whenever no EmailTemplate row exists for a (Key, Audience)
// and whenever a stored row fails to render, so they must always be valid.
public static class EmailTemplateDefaults
{
    public static EmailTemplateContent Get(EmailTemplateKey key) => key switch
    {
        EmailTemplateKey.PasswordReset => new(
            "Reset your {{AppName}} password",
            "<p>Someone requested a password reset for your account.</p>" +
            "<p><a href=\"{{ResetLink}}\">Reset your password</a></p>" +
            "<p>If this wasn't you, you can safely ignore this email.</p>"),

        EmailTemplateKey.EmailConfirmation => new(
            "Confirm your {{AppName}} email",
            "<p>Welcome to {{AppName}}. Please confirm your email address to finish setting up your account.</p>" +
            "<p><a href=\"{{ConfirmLink}}\">Confirm your email</a></p>" +
            "<p>If you didn't create this account, you can safely ignore this email.</p>"),

        EmailTemplateKey.TripSubmitted => new(
            "Trip awaiting your approval: {{Trip.Purpose}}",
            "<p>{{Traveler.Name}} submitted a trip for your approval.</p>" +
            "<p><strong>{{Trip.Purpose}}</strong><br>" +
            "{{Trip.Dates}}<br>" +
            "Trip code: {{Trip.Code}}</p>" +
            "<p><a href=\"{{ApprovalLink}}\">Review and approve or reject this trip</a></p>"),

        EmailTemplateKey.TripApproved => new(
            "Your trip was approved: {{Trip.Purpose}}",
            "<p>Your trip <strong>{{Trip.Purpose}}</strong> ({{Trip.Dates}}) " +
            "was approved by {{Approver.Name}}.</p>" +
            "<p>Trip code: {{Trip.Code}}</p>" +
            "{{CommentBlock}}"),

        EmailTemplateKey.TripRejected => new(
            "Your trip was rejected: {{Trip.Purpose}}",
            "<p>Your trip <strong>{{Trip.Purpose}}</strong> ({{Trip.Dates}}) " +
            "was rejected by {{Approver.Name}}.</p>" +
            "<p>Trip code: {{Trip.Code}}</p>" +
            "{{CommentBlock}}"),

        EmailTemplateKey.Invitation => new(
            "You're invited to {{AppName}}",
            "<p>{{InvitedBy.Name}} has invited {{RecipientEmail}} to join {{AppName}} as {{Invite.Role}}.</p>" +
            "<p><a href=\"{{InviteLink}}\">Accept your invitation</a></p>" +
            "<p>This link expires on {{Invite.ExpiresAt}}. If you weren't expecting it, you can safely ignore this email.</p>"),

        _ => throw new ArgumentOutOfRangeException(nameof(key), key, "No default template."),
    };
}
