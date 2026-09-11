using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using TravelTracker.Web.Data.Entities;
using TravelTracker.Web.Services.Email;

namespace TravelTracker.Web.Services;

// Shared "send an email-confirmation link" step, used by self-serve registration
// and the resend page. Mirrors the ForgotPassword token pattern (mint -> base64url
// encode -> send) but kept in one place since two callers need it. Link building
// (Url helper + request scheme) stays with the caller, so this stays HTTP-free and
// unit-testable; the caller passes a factory that turns (userId, encodedToken) into
// an absolute URL.
public class EmailConfirmationService
{
    private readonly UserManager<AppUser> _users;
    private readonly IEmailTemplateService _email;

    public EmailConfirmationService(UserManager<AppUser> users, IEmailTemplateService email)
    {
        _users = users;
        _email = email;
    }

    public async Task SendLinkAsync(AppUser user, Func<string, string, string> linkFactory)
    {
        var token = await _users.GenerateEmailConfirmationTokenAsync(user);
        var encoded = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        var link = linkFactory(user.Id, encoded);

        await _email.SendAsync(EmailTemplateKey.EmailConfirmation, user.Email!,
            new Dictionary<string, string?>
            {
                [EmailTemplateTokens.RecipientName] = user.DisplayName,
                ["ConfirmLink"] = link,
            });
    }

    // Decode a base64url token back to the raw Identity token for ConfirmEmailAsync.
    public static string DecodeToken(string encoded) =>
        Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(encoded));
}
