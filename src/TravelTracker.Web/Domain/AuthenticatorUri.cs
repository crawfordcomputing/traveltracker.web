using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;

namespace TravelTracker.Web.Domain;

// Pure helpers for presenting an authenticator (TOTP) shared key during enrollment:
// the otpauth:// provisioning URI encoded into the QR image, and the human-readable
// grouped key for manual entry. No Identity/DB dependency, so both are unit-testable.
public static class AuthenticatorUri
{
    // Shown as the account label and QR issuer in the authenticator app.
    public const string Issuer = "Travel Tracker";

    // otpauth://totp/{issuer}:{email}?secret=...&issuer=...&digits=6 — the standard Key
    // Uri Format read by Google Authenticator, Microsoft Authenticator, Authy, 1Password.
    public static string Build(string email, string unformattedKey)
        => string.Format(
            CultureInfo.InvariantCulture,
            "otpauth://totp/{0}:{1}?secret={2}&issuer={0}&digits=6",
            UrlEncoder.Default.Encode(Issuer),
            UrlEncoder.Default.Encode(email),
            unformattedKey);

    // Groups the key into lowercase 4-char blocks so it is easier to type by hand.
    public static string FormatKey(string unformattedKey)
    {
        var result = new StringBuilder();
        for (int i = 0; i < unformattedKey.Length; i += 4)
            result.Append(unformattedKey.AsSpan(i, Math.Min(4, unformattedKey.Length - i)))
                  .Append(' ');
        return result.ToString().ToLowerInvariant().Trim();
    }
}
