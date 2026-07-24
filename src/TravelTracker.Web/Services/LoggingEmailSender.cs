using Microsoft.Extensions.Logging;

namespace TravelTracker.Web.Services;

// Default sender for dev/eval: no SMTP required. Writes the whole message to the
// log so a developer can copy the password-reset link straight out of the console.
// Never wire this up in production — the body (which contains one-time links) lands
// in the app log.
public class LoggingEmailSender : IEmailSender
{
    private readonly ILogger<LoggingEmailSender> _logger;

    public LoggingEmailSender(ILogger<LoggingEmailSender> logger) => _logger = logger;

    public Task SendAsync(string recipient, string subject, string htmlBody)
    {
        _logger.LogInformation(
            "[DEV EMAIL] To: {To}\nSubject: {Subject}\n{Body}", recipient, subject, htmlBody);
        return Task.CompletedTask;
    }
}
