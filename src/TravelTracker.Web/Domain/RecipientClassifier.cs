using TravelTracker.Web.Data.Entities;

namespace TravelTracker.Web.Domain;

// Decides whether an email recipient is Internal (on one of "our" domains) or
// External, so the template layer can pick the audience-specific wording
// (ADR-0005). Pure: takes the configured domain lists, no I/O.
//
// Exact, case-insensitive host match only. Subdomains must be listed explicitly
// (corp.example.com) so lookalikes such as example.com.evil.io never match.
public static class RecipientClassifier
{
    public const string InternalDomainsKey = "Email:InternalDomains";
    public const string FallbackDomainsKey = "Auth:Registration:AllowedDomains";

    public static EmailAudience Classify(IConfiguration config, string? email)
        => Classify(InternalDomains(config), email);

    public static EmailAudience Classify(IReadOnlyCollection<string> internalDomains, string? email)
    {
        // No configured list means we can't tell; every recipient gets the Any variant.
        if (internalDomains.Count == 0) return EmailAudience.Any;

        var host = DomainOf(email);
        if (host is null) return EmailAudience.External;

        return internalDomains.Any(d => string.Equals(d, host, StringComparison.OrdinalIgnoreCase))
            ? EmailAudience.Internal
            : EmailAudience.External;
    }

    // Email:InternalDomains, else Auth:Registration:AllowedDomains (already "our"
    // domains for self-registration), else empty. Blank entries are ignored.
    public static IReadOnlyList<string> InternalDomains(IConfiguration config)
    {
        var list = Read(config, InternalDomainsKey);
        if (list.Count == 0) list = Read(config, FallbackDomainsKey);
        return list;
    }

    private static List<string> Read(IConfiguration config, string key) =>
        (config.GetSection(key).Get<string[]>() ?? Array.Empty<string>())
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .Select(d => d.Trim().ToLowerInvariant())
            .ToList();

    // Lowercased host part of an email, or null if it isn't a usable address.
    public static string? DomainOf(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return null;
        var at = email.LastIndexOf('@');
        if (at < 0 || at == email.Length - 1) return null;
        var host = email[(at + 1)..].Trim().ToLowerInvariant();
        return host.Length == 0 ? null : host;
    }
}
