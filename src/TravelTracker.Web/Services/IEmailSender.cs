namespace TravelTracker.Web.Services;

// Minimal transactional-email port. Password reset is the first caller (M1.5);
// M5 notifications will reuse it. Kept deliberately tiny so the dev implementation
// (log the message) and the prod implementation (SMTP) are both trivial.
public interface IEmailSender
{
    Task SendAsync(string recipient, string subject, string htmlBody);
}
