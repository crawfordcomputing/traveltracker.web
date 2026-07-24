using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using TravelTracker.Web.Data.Entities;

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
    private readonly IEmailSender _email;

    public EmailConfirmationService(UserManager<AppUser> users, IEmailSender email)
    {
        _users = users;
        _email = email;
    }

    public async Task SendLinkAsync(AppUser user, Func<string, string, string> linkFactory)
    {
        var token = await _users.GenerateEmailConfirmationTokenAsync(user);
        var encoded = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        var link = linkFactory(user.Id, encoded);

        await _email.SendAsync(user.Email!, "Confirm your Travel Tracker email",
            "<p>Welcome to Travel Tracker. Please confirm your email address to finish setting up your account.</p>" +
            $"<p><a href=\"{link}\">Confirm your email</a></p>" +
            "<p>If you didn't create this account, you can safely ignore this email.</p>");
    }

    // Decode a base64url token back to the raw Identity token for ConfirmEmailAsync.
    public static string DecodeToken(string encoded) =>
        Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(encoded));
}
