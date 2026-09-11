using TravelTracker.Web.Data.Entities;

namespace TravelTracker.Web.Services;

// Minimal transactional-email port. Password reset is the first caller (M1.5);
// M5 notifications will reuse it. Kept deliberately tiny so the dev implementation
// (log the message) and the prod implementation (SMTP) are both trivial.
public interface IEmailSender
{
    Task SendAsync(string recipient, string subject, string htmlBody);

    // Template-aware overload (ADR-0005). Only the auditing decorator cares about
    // the origin, so it's a default implementation that concrete senders and test
    // fakes never have to know about.
    Task SendAsync(string recipient, string subject, string htmlBody, EmailOrigin? origin)
        => SendAsync(recipient, subject, htmlBody);
}

// Which template (and audience variant) an email was rendered from, recorded on
// NotificationLog so admins can see what was sent without storing the body.
public sealed record EmailOrigin(EmailTemplateKey Key, EmailAudience Audience);
