using System.Security.Cryptography;

namespace TravelTracker.Web.Domain;

// Invite token creation + hashing, shared by the admin Invites page (which mints
// tokens) and the Register page (which verifies them). The raw token is a URL-safe
// random string; only its hash is persisted.
public static class InviteTokens
{
    // 32 bytes of entropy, base64url-encoded (no padding) so it drops cleanly into
    // a query string.
    public static string NewToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    // Deterministic SHA-256 hex hash for storage + lookup.
    public static string Hash(string token)
    {
        var digest = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(digest); // uppercase hex, stable
    }
}
