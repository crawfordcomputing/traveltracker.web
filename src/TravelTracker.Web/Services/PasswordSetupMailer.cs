using System.Text;

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using TravelTracker.Web.Auth;
using TravelTracker.Web.Data.Entities;
using TravelTracker.Web.Services.Email;

namespace TravelTracker.Web.Services;

// Mints and delivers the one-time password links an admin issues: account setup on
// user create, and admin-initiated reset. Replaces the old admin-generated temporary
// password, which never expired and travelled over chat or email as a live credential.
//
// The token is an Identity password-reset token from PasswordSetupTokenProvider, so
// it expires (Auth:PasswordSetup:LifetimeHours, default 24) and dies on first use
// when the reset rotates the user's security stamp.
public sealed class PasswordSetupMailer
{
    private readonly UserManager<AppUser> _users;
    private readonly IEmailTemplateService _email;
    private readonly ILogger<PasswordSetupMailer> _logger;
    private readonly IOptions<PasswordSetupTokenProviderOptions> _tokenOptions;

    public PasswordSetupMailer(
        UserManager<AppUser> users,
        IEmailTemplateService email,
        ILogger<PasswordSetupMailer> logger,
        IOptions<PasswordSetupTokenProviderOptions> tokenOptions)
    {
        _users = users;
        _email = email;
        _logger = logger;
        _tokenOptions = tokenOptions;
    }

    public TimeSpan Lifetime => _tokenOptions.Value.TokenLifespan;

    // "24 hours" / "1 hour" — for email copy and the admin banner.
    public string LifetimeText
    {
        get
        {
            var hours = (int)Math.Round(Lifetime.TotalHours);
            return hours == 1 ? "1 hour" : $"{hours} hours";
        }
    }

    // Base64url-encoded so the token survives a query string, matching the encoding
    // the ResetPassword page decodes (and the self-service forgot-password flow).
    public async Task<string> CreateTokenAsync(AppUser user)
    {
        var token = await _users.GeneratePasswordResetTokenAsync(user);
        return WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
    }

    public Task<bool> TrySendAccountSetupAsync(AppUser user, string link) =>
        TrySendAsync(user, EmailTemplateKey.AccountSetup, new Dictionary<string, string?>
        {
            [EmailTemplateTokens.RecipientName] = user.DisplayName,
            ["SetupLink"] = link,
            ["Setup.ExpiresIn"] = LifetimeText,
        });

    public Task<bool> TrySendPasswordResetAsync(AppUser user, string link) =>
        TrySendAsync(user, EmailTemplateKey.PasswordReset, new Dictionary<string, string?>
        {
            [EmailTemplateTokens.RecipientName] = user.DisplayName,
            ["ResetLink"] = link,
        });

    // Best-effort, like the invite mail: the admin also sees the link on screen, so a
    // mail outage must not fail the request or strand a just-created account.
    private async Task<bool> TrySendAsync(
        AppUser user, EmailTemplateKey key, IReadOnlyDictionary<string, string?> tokens)
    {
        if (string.IsNullOrWhiteSpace(user.Email)) return false;

        try
        {
            await _email.SendAsync(key, user.Email, tokens);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{Template} email to {Recipient} failed.", key, user.Email);
            return false;
        }
    }
}
