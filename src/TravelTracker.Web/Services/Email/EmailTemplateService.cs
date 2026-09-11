using Microsoft.EntityFrameworkCore;
using TravelTracker.Web.Data;
using TravelTracker.Web.Data.Entities;
using TravelTracker.Web.Domain;

namespace TravelTracker.Web.Services.Email;

// Template layer above IEmailSender (ADR-0005). Callers pass a template key and
// token values instead of building HTML; this classifies the recipient, picks the
// stored override or built-in default, renders, and hands off to IEmailSender.
// Failure semantics are unchanged from the sender: exceptions propagate, and it's
// the caller's job to swallow (TrySendAsync) where delivery is best-effort.
public interface IEmailTemplateService
{
    Task SendAsync(EmailTemplateKey key, string recipient, IReadOnlyDictionary<string, string?> tokens);

    // Admin "send test to me": renders the given (possibly unsaved) template with
    // sample data and sends it through the normal pipeline, so it shows up in the
    // notification log. Throws EmailTemplateException if the template is invalid.
    Task SendTestAsync(EmailTemplateKey key, EmailAudience audience, EmailTemplateContent content, string recipient);

    // Renders a template against sample data for the admin preview.
    EmailTemplateContent RenderSample(EmailTemplateKey key, EmailTemplateContent content, string recipient);

    // Which override/default applies to (key, audience): the stored row for that
    // audience, else the (key, Any) row, else the built-in default.
    Task<ResolvedTemplate> ResolveAsync(EmailTemplateKey key, EmailAudience audience);
}

// The template chosen for a send. StoredAudience is the audience of the override
// row that won, or null when the built-in default applies.
public sealed record ResolvedTemplate(EmailTemplateContent Content, EmailAudience? StoredAudience)
{
    public bool IsDefault => StoredAudience is null;
}

public class EmailTemplateService : IEmailTemplateService
{
    public const string AppNameKey = "Email:AppName";
    public const string DefaultAppName = "Travel Tracker";

    private readonly AppDbContext _db;
    private readonly IEmailSender _sender;
    private readonly IConfiguration _config;
    private readonly ILogger<EmailTemplateService> _logger;

    public EmailTemplateService(AppDbContext db, IEmailSender sender, IConfiguration config,
        ILogger<EmailTemplateService> logger)
    {
        _db = db;
        _sender = sender;
        _config = config;
        _logger = logger;
    }

    public string AppName => _config[AppNameKey] is { Length: > 0 } n ? n : DefaultAppName;

    public async Task SendAsync(EmailTemplateKey key, string recipient,
        IReadOnlyDictionary<string, string?> tokens)
    {
        var audience = RecipientClassifier.Classify(_config, recipient);
        var resolved = await ResolveAsync(key, audience);
        var values = WithCommon(tokens, recipient);

        EmailTemplateContent rendered;
        try
        {
            rendered = EmailTemplateRenderer.Render(key, resolved.Content, values);
        }
        catch (EmailTemplateException ex) when (!resolved.IsDefault)
        {
            // A stored template that no longer renders (data drift across versions)
            // must never block a password reset: warn and send the built-in default.
            _logger.LogWarning(ex,
                "Stored email template {Key}/{Audience} failed to render; sending the built-in default.",
                key, resolved.StoredAudience);
            rendered = EmailTemplateRenderer.Render(key, EmailTemplateDefaults.Get(key), values);
        }

        await _sender.SendAsync(recipient, rendered.Subject, rendered.HtmlBody, new EmailOrigin(key, audience));
    }

    public async Task SendTestAsync(EmailTemplateKey key, EmailAudience audience,
        EmailTemplateContent content, string recipient)
    {
        var rendered = RenderSample(key, content, recipient);
        await _sender.SendAsync(recipient, "[Test] " + rendered.Subject, rendered.HtmlBody,
            new EmailOrigin(key, audience));
    }

    public EmailTemplateContent RenderSample(EmailTemplateKey key, EmailTemplateContent content, string recipient) =>
        EmailTemplateRenderer.Render(key, content, WithCommon(EmailTemplateTokens.Sample(key), recipient));

    public async Task<ResolvedTemplate> ResolveAsync(EmailTemplateKey key, EmailAudience audience)
    {
        var rows = await _db.EmailTemplates.AsNoTracking()
            .Where(t => t.Key == key && (t.Audience == audience || t.Audience == EmailAudience.Any))
            .ToListAsync();

        var row = rows.FirstOrDefault(t => t.Audience == audience)
               ?? rows.FirstOrDefault(t => t.Audience == EmailAudience.Any);

        return row is null
            ? new ResolvedTemplate(EmailTemplateDefaults.Get(key), null)
            : new ResolvedTemplate(new EmailTemplateContent(row.Subject, row.HtmlBody), row.Audience);
    }

    // AppName and RecipientEmail are always known here; callers only supply the
    // tokens that need their context. Caller values win if they overlap.
    private Dictionary<string, string?> WithCommon(IReadOnlyDictionary<string, string?> tokens, string recipient)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [EmailTemplateTokens.AppName] = AppName,
            [EmailTemplateTokens.RecipientEmail] = recipient,
        };
        foreach (var (k, v) in tokens) values[k] = v;
        return values;
    }
}
