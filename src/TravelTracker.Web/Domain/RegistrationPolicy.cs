namespace TravelTracker.Web.Domain;

// How new local accounts may be created. Mirrors the "sensible default, one config
// switch" pattern used for Auth:EnableEntraId.
//   Open   - anyone may self-register (demo/eval; set in Development only)
//   Domain - self-register allowed only for an approved email domain
//   Invite - no self-serve; an admin issues an invite token per user
//   Closed - no self-serve at all (admins create every account)
public enum RegistrationMode { Open, Domain, Invite, Closed }

// Pure config reads — no DB, no HTTP — so this is trivially unit-testable.
public static class RegistrationPolicy
{
    // Unknown/missing values fail closed: better to lock registration than to
    // accidentally leave it wide open.
    public static RegistrationMode Mode(IConfiguration config) =>
        Enum.TryParse<RegistrationMode>(config["Auth:Registration:Mode"], ignoreCase: true, out var m)
            ? m
            : RegistrationMode.Closed;

    // The public /Account/Register page is reachable only when self-serve makes
    // sense. Invite still uses that page but reaches it via a token link, so it is
    // gated separately (SelfServeAllowed is false for Invite).
    public static bool SelfServeAllowed(IConfiguration config) =>
        Mode(config) is RegistrationMode.Open or RegistrationMode.Domain;

    // Whether a given email may self-register under the current mode.
    public static bool EmailAllowed(IConfiguration config, string? email)
    {
        switch (Mode(config))
        {
            case RegistrationMode.Open:
                return true;

            case RegistrationMode.Domain:
                var host = DomainOf(email);
                if (host is null) return false;
                var allowed = config.GetSection("Auth:Registration:AllowedDomains")
                    .Get<string[]>() ?? Array.Empty<string>();
                return allowed.Any(d =>
                    string.Equals(d?.Trim(), host, StringComparison.OrdinalIgnoreCase));

            default: // Invite, Closed — never via self-serve
                return false;
        }
    }

    // Lowercased host part of an email, or null if it isn't a usable address.
    private static string? DomainOf(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return null;
        var at = email.LastIndexOf('@');
        if (at < 0 || at == email.Length - 1) return null;
        var host = email[(at + 1)..].Trim().ToLowerInvariant();
        return host.Length == 0 ? null : host;
    }
}
