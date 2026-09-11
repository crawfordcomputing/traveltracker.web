using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TravelTracker.Web.Data;
using TravelTracker.Web.Data.Entities;
using TravelTracker.Web.Services;
using TravelTracker.Web.Services.Email;

namespace TravelTracker.Tests;

// Records outbound mail at the IEmailSender seam, including the template origin
// the auditing decorator would log. Optionally throws to simulate a mail outage.
internal sealed class RecordingEmailSender : IEmailSender
{
    public Exception? Throw { get; init; }
    public List<(string To, string Subject, string Body, EmailOrigin? Origin)> Sent { get; } = new();

    public Task SendAsync(string recipient, string subject, string htmlBody)
        => SendAsync(recipient, subject, htmlBody, origin: null);

    public Task SendAsync(string recipient, string subject, string htmlBody, EmailOrigin? origin)
    {
        if (Throw is not null) return Task.FromException(Throw);
        Sent.Add((recipient, subject, htmlBody, origin));
        return Task.CompletedTask;
    }
}

// For handler tests whose assertions don't involve email at all.
internal sealed class NoopEmailTemplateService : IEmailTemplateService
{
    public Task SendAsync(EmailTemplateKey key, string recipient, IReadOnlyDictionary<string, string?> tokens)
        => Task.CompletedTask;

    public Task SendTestAsync(EmailTemplateKey key, EmailAudience audience, EmailTemplateContent content, string recipient)
        => Task.CompletedTask;

    public EmailTemplateContent RenderSample(EmailTemplateKey key, EmailTemplateContent content, string recipient)
        => content;

    public Task<ResolvedTemplate> ResolveAsync(EmailTemplateKey key, EmailAudience audience)
        => Task.FromResult(new ResolvedTemplate(EmailTemplateDefaults.Get(key), null));
}

internal static class EmailTemplateServices
{
    // The real template service over a real DB and a fake sender.
    public static EmailTemplateService Real(AppDbContext db, IEmailSender sender,
        IDictionary<string, string?>? settings = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(settings ?? new Dictionary<string, string?>())
            .Build();
        return new EmailTemplateService(db, sender, config, NullLogger<EmailTemplateService>.Instance);
    }
}
