using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TravelTracker.Web.Data.Entities;
using TravelTracker.Web.Services;

namespace TravelTracker.Web.Pages.Account;

// Handles the Microsoft Entra ID (OpenID Connect) round-trip. Only reachable
// when Auth:EnableEntraId=true — the button that posts here is hidden otherwise.
//
// Excluded from coverage: the whole handler is an external OIDC round-trip
// (redirects to Entra, consumes the external login callback), which needs a live
// identity provider to exercise. Verified via manual / integration sign-in.
[ExcludeFromCodeCoverage]
[AllowAnonymous]
public class ExternalLoginModel : PageModel
{
    private readonly SignInManager<AppUser> _signInManager;
    private readonly UserManager<AppUser> _userManager;
    private readonly EntraRoleSynchronizer _roleSync;

    public ExternalLoginModel(
        SignInManager<AppUser> signInManager,
        UserManager<AppUser> userManager,
        EntraRoleSynchronizer roleSync)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _roleSync = roleSync;
    }

    // Persisted across the RedirectToPage back to Login so the failure reason
    // survives the new request instead of being silently dropped.
    [TempData] public string? ErrorMessage { get; set; }

    // Step 1: kick off the challenge to the external provider.
    public IActionResult OnPost(string provider, string? returnUrl = null)
    {
        var redirectUrl = Url.Page("/Account/ExternalLogin", pageHandler: "Callback",
            values: new { returnUrl });
        var properties = _signInManager.ConfigureExternalAuthenticationProperties(provider, redirectUrl);
        return new ChallengeResult(provider, properties);
    }

    // Step 2: provider redirects back here; sign in or auto-provision a local user.
    public async Task<IActionResult> OnGetCallbackAsync(string? returnUrl = null, string? remoteError = null)
    {
        returnUrl ??= Url.Content("~/");
        if (remoteError is not null)
        {
            ErrorMessage = $"Error from external provider: {remoteError}";
            return RedirectToPage("/Account/Login");
        }

        var info = await _signInManager.GetExternalLoginInfoAsync();
        if (info is null) return RedirectToPage("/Account/Login");

        var signInResult = await _signInManager.ExternalLoginSignInAsync(
            info.LoginProvider, info.ProviderKey, isPersistent: false, bypassTwoFactor: true);
        if (signInResult.Succeeded)
        {
            // Returning user: reconcile roles against their current Entra groups so a
            // promotion/demotion in the directory takes effect on login, not by hand.
            var linkedUser = await _userManager.FindByLoginAsync(info.LoginProvider, info.ProviderKey);
            if (linkedUser is not null && await _roleSync.SyncAsync(linkedUser, info.Principal))
                await _signInManager.RefreshSignInAsync(linkedUser);
            return LocalRedirect(returnUrl);
        }

        // First time this external identity is seen — provision a local Employee.
        var email = info.Principal.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
                    ?? info.Principal.FindFirst("preferred_username")?.Value;
        if (string.IsNullOrEmpty(email))
        {
            ErrorMessage = "The external provider did not supply an email address.";
            return RedirectToPage("/Account/Login");
        }

        // A local account with this email already exists but is NOT linked to this
        // external identity (ExternalLoginSignInAsync above would have signed us in
        // otherwise). Refuse to auto-link on an email match: an attacker could
        // pre-register a local account with the victim's email and hijack the
        // external identity. Require the user to sign in with their existing
        // credentials and link from account settings instead.
        var existingUser = await _userManager.FindByEmailAsync(email);
        if (existingUser is not null)
        {
            ErrorMessage = "An account with this email already exists. Sign in with your " +
                           "existing credentials to link Microsoft Entra ID.";
            return RedirectToPage("/Account/Login");
        }

        // First time we've seen this identity and no local account owns the email:
        // provision a fresh Employee and link the external login.
        var user = new AppUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            DisplayName = info.Principal.Identity?.Name ?? email
        };
        var created = await _userManager.CreateAsync(user);
        if (!created.Succeeded)
        {
            ErrorMessage = "Could not create a local account for the external login.";
            return RedirectToPage("/Account/Login");
        }
        await _userManager.AddToRoleAsync(user, Roles.Employee);

        var linked = await _userManager.AddLoginAsync(user, info);
        if (!linked.Succeeded)
        {
            ErrorMessage = "Could not link the external login to the new account.";
            return RedirectToPage("/Account/Login");
        }

        // Apply Entra group->role mappings before the cookie is issued so the new
        // account lands with its directory-assigned roles, not just the Employee floor.
        await _roleSync.SyncAsync(user, info.Principal);

        await _signInManager.SignInAsync(user, isPersistent: false);
        return LocalRedirect(returnUrl);
    }
}
