using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using TravelTracker.Web.Data.Entities;

namespace TravelTracker.Web.Auth;

// Options for the password-setup/reset token provider. Kept as its own options type
// so Auth:PasswordSetup:LifetimeHours governs ONLY password links: the shared
// DataProtectionTokenProviderOptions also drives email-confirmation and change-email
// tokens, and shortening one must not silently shorten the others.
public sealed class PasswordSetupTokenProviderOptions : DataProtectionTokenProviderOptions
{
    public PasswordSetupTokenProviderOptions()
    {
        Name = PasswordSetupTokenProvider.ProviderName;
        TokenLifespan = TimeSpan.FromHours(24);
    }
}

// The provider behind every password link the app issues: self-service "Forgot
// password", admin "Create user" (account setup) and admin "Reset password".
// Single-use by construction — a successful reset rotates the security stamp the
// token is bound to, so the link cannot be replayed.
public sealed class PasswordSetupTokenProvider : DataProtectorTokenProvider<AppUser>
{
    public const string ProviderName = "PasswordSetup";

    public PasswordSetupTokenProvider(
        IDataProtectionProvider dataProtectionProvider,
        IOptions<PasswordSetupTokenProviderOptions> options,
        ILogger<PasswordSetupTokenProvider> logger)
        : base(dataProtectionProvider, options, logger)
    {
    }
}
