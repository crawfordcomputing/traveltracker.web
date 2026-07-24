using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TravelTracker.Web.Data.Entities;

namespace TravelTracker.Web.Data;

// Refuses sign-in for deactivated accounts. CanSignInAsync is the hook Identity
// calls inside PasswordSignInAsync (covers local login) and inside the cookie
// security-stamp revalidation (covers already-issued cookies and Entra sessions),
// so flipping IsActive off both blocks new logins and — once the revalidation
// interval elapses — kicks live sessions.
public class AppSignInManager : SignInManager<AppUser>
{
    public AppSignInManager(
        UserManager<AppUser> userManager,
        IHttpContextAccessor contextAccessor,
        IUserClaimsPrincipalFactory<AppUser> claimsFactory,
        IOptions<IdentityOptions> optionsAccessor,
        ILogger<SignInManager<AppUser>> logger,
        IAuthenticationSchemeProvider schemes,
        IUserConfirmation<AppUser> confirmation)
        : base(userManager, contextAccessor, claimsFactory, optionsAccessor,
               logger, schemes, confirmation)
    {
    }

    public override async Task<bool> CanSignInAsync(AppUser user)
    {
        if (!user.IsActive) return false;
        return await base.CanSignInAsync(user);
    }
}
