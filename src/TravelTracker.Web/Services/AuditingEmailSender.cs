using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TravelTracker.Web.Data;
using TravelTracker.Web.Data.Entities;

namespace TravelTracker.Web.Services;

// Decorator over IEmailSender that records every send attempt to NotificationLog,
// so admins can confirm delivery and spot failures in-app (Pages/Admin/Notifications)
// without opening Application Insights.
//
// Behaviour-preserving: it records the outcome and then re-throws any exception, so
// existing callers are unaffected - Trips/Details.TrySendAsync still swallows for
// best-effort UX, while password-reset / email-confirmation still propagate as before.
// Persisting the audit row is itself best-effort: a logging-store failure must never
// change the outcome the caller sees.
public class AuditingEmailSender : IEmailSender
{
    private readonly IEmailSender _inner;
    private readonly AppDbContext _db;
    private readonly ILogger<AuditingEmailSender> _logger;

    public AuditingEmailSender(IEmailSender inner, AppDbContext db, ILogger<AuditingEmailSender> logger)
    {
        _inner = inner;
        _db = db;
        _logger = logger;
    }

    // The wrapped concrete sender (LoggingEmailSender / SmtpEmailSender).
    // Exposed so the Email:Provider selection stays unit-testable through the decorator.
    public IEmailSender Inner => _inner;

    public Task SendAsync(string recipient, string subject, string htmlBody)
        => SendAsync(recipient, subject, htmlBody, origin: null);

    public async Task SendAsync(string recipient, string subject, string htmlBody, EmailOrigin? origin)
    {
        var log = new NotificationLog
        {
            SentAt = DateTimeOffset.UtcNow,
            Recipient = recipient,
            // Rendered subjects can outgrow the 256-char template limit once tokens
            // expand; clip rather than lose the audit row to a column-length error.
            Subject = Truncate(subject, 256),
            TemplateKey = origin?.Key,
            Audience = origin?.Audience,
        };

        try
        {
            await _inner.SendAsync(recipient, subject, htmlBody, origin);
            log.Status = NotificationStatus.Sent;
        }
        catch (Exception ex)
        {
            log.Status = NotificationStatus.Failed;
            log.Error = Truncate(ex.Message, 2000); // message only - never the body
            await RecordAsync(log);
            throw;
        }

        await RecordAsync(log);
    }

    private async Task RecordAsync(NotificationLog log)
    {
        try
        {
            _db.NotificationLogs.Add(log);
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            // Detach the failed row so it is not retried by (and does not fail) any
            // later SaveChangesAsync on this request-scoped context.
            _db.Entry(log).State = EntityState.Detached;
            _logger.LogError(ex,
                "Failed to persist NotificationLog for {Recipient} ({Status}).",
                log.Recipient, log.Status);
        }
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
