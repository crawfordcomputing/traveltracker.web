using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TravelTracker.Web.Data;
using TravelTracker.Web.Data.Entities;
using TravelTracker.Web.Domain;
using TravelTracker.Web.Services.Email;

namespace TravelTracker.Web.Pages.Admin.EmailTemplates;

// Admin-editable email templates (ADR-0005). Under /Admin (RequireAdmin). One row
// per email with a Default/Customized chip for each audience variant.
public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;

    public IndexModel(AppDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    public record Row(EmailTemplateKey Key, string Title, string Description,
        IReadOnlyDictionary<EmailAudience, bool> Customized);

    public List<Row> Rows { get; private set; } = new();
    public IReadOnlyList<string> InternalDomains { get; private set; } = Array.Empty<string>();
    public bool UsingFallbackDomains { get; private set; }

    public static readonly EmailAudience[] Audiences =
        { EmailAudience.Any, EmailAudience.Internal, EmailAudience.External };

    public async Task OnGetAsync()
    {
        var stored = await _db.EmailTemplates.AsNoTracking()
            .Select(t => new { t.Key, t.Audience })
            .ToListAsync();
        var set = stored.Select(s => (s.Key, s.Audience)).ToHashSet();

        Rows = EmailTemplateTokens.Keys
            .Select(k =>
            {
                var spec = EmailTemplateTokens.For(k);
                return new Row(k, spec.Title, spec.Description,
                    Audiences.ToDictionary(a => a, a => set.Contains((k, a))));
            })
            .ToList();

        InternalDomains = RecipientClassifier.InternalDomains(_config);
        UsingFallbackDomains = InternalDomains.Count > 0 &&
            !(_config.GetSection(RecipientClassifier.InternalDomainsKey).Get<string[]>() ?? Array.Empty<string>())
                .Any(d => !string.IsNullOrWhiteSpace(d));
    }
}
