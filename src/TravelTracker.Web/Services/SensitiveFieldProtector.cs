using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace TravelTracker.Web.Services;

// Field-level encryption for sensitive traveler identifiers (passport number, Known
// Traveler Number). Wraps ASP.NET Core Data Protection so those columns hold
// ciphertext at rest and are only ever decrypted in-process when an admin or the
// traveler edits their profile.
//
// Key management: Data Protection keys live wherever the app's key ring is
// configured. On Azure App Service the default file provider writes them under
// /home (persisted), which is adequate for a single app; a multi-instance or
// hardened deployment should point the key ring at Azure Blob + Key Vault. Rotating
// or losing the key ring makes existing ciphertext undecryptable (Unprotect returns
// null), which is the intended fail-safe — it never throws into a page.
public interface ISensitiveFieldProtector
{
    // Encrypts plaintext for storage. Null/empty in -> null out (store nothing).
    string? Protect(string? plaintext);

    // Decrypts stored ciphertext. Null/empty in -> null out. Undecryptable input
    // (tampered, or encrypted under a lost key) -> null, never an exception.
    string? Unprotect(string? ciphertext);
}

public sealed class SensitiveFieldProtector : ISensitiveFieldProtector
{
    private readonly IDataProtector _protector;

    public SensitiveFieldProtector(IDataProtectionProvider provider)
        => _protector = provider.CreateProtector("TravelTracker.PersonalData.v1");

    public string? Protect(string? plaintext)
        => string.IsNullOrEmpty(plaintext) ? null : _protector.Protect(plaintext);

    public string? Unprotect(string? ciphertext)
    {
        if (string.IsNullOrEmpty(ciphertext)) return null;
        try
        {
            return _protector.Unprotect(ciphertext);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }
}
