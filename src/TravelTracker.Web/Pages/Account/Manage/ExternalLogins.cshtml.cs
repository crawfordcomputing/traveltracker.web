using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TravelTracker.Web.Data;
using TravelTracker.Web.Data.Entities;
using TravelTracker.Web.Services;

namespace TravelTracker.Web.Pages.Account.Manage;

// Self-service linking of an external identity (Microsoft Entra ID) to the signed-in
// local account. This is the sanctioned counterpart to the ExternalLogin callback's
// refusal to auto-link on an email match: the user proves ownership of both sides —
// they are already signed in locally, then complete a fresh Entra round-trip — so no
// attacker who merely knows the victim's email can hijack the external identity.
//
// The /Account/Manage folder is authorized in Program.cs, so every handler here runs
// as an authenticated user; the link callback is scoped to that user id.
[ExcludeFromCodeCoverage] // external OIDC round-trip; needs a live IdP to exercise.
public class ExternalLoginsModel : PageModel
{
    private readonly UserManager<AppUser> _userManager;
    private readonly SignInManager<AppUser> _signInManager;
    private readonly IUserStore<AppUser> _userStore;
    private readonly EntraRoleSynchronizer _roleSync;
    private readonly IConfiguration _config;

    public ExternalLoginsModel(
        UserManager<AppUser> userManager,
        SignInManager<AppUser> signInManager,
        IUserStore<AppUser> userStore,
        EntraRoleSynchronizer roleSync,
        IConfiguration config)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _userStore = userStore;
        _roleSync = roleSync;
        _config = config;
    }

    // The provider name registered in AuthSetup.AddOpenIdConnect("EntraId", ...).
    private const string EntraProvider = "EntraId";

    public IList<UserLoginInfo> CurrentLogins { get; set; } = new List<UserLoginInfo>();
    public bool EntraIdEnabled { get; set; }
    public bool EntraLinked { get; set; }

    // Removing an external login is only safe when the account keeps another way in,
    // otherwise the user locks themselves out. A local password counts.
    public bool ShowRemoveButton { get; set; }

    [TempData] public string? StatusMessage { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return NotFound();
        await LoadAsync(user);
        return Page();
    }

    // Step 1: start a fresh challenge to Entra, scoped to this user id so the
    // callback below can only ever link the identity to the account that initiated it.
    public async Task<IActionResult> OnPostLinkLoginAsync()
    {
        // Clear any lingering external cookie so a stale principal can't be linked.
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);

        var redirectUrl = Url.Page("./ExternalLogins", pageHandler: "LinkLoginCallback");
        var properties = _signInManager.ConfigureExternalAuthenticationProperties(
            EntraProvider, redirectUrl, _userManager.GetUserId(User));
        return new ChallengeResult(EntraProvider, properties);
    }

    // Step 2: Entra redirects back; attach the returned identity to this account.
    public async Task<IActionResult> OnGetLinkLoginCallbackAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return NotFound();

        var info = await _signInManager.GetExternalLoginInfoAsync(await _userManager.GetUserIdAsync(user));
        if (info is null)
        {
            StatusMessage = "Error: could not read the Microsoft Entra ID sign-in. Please try again.";
            return RedirectToPage();
        }

        var result = await _userManager.AddLoginAsync(user, info);
        if (!result.Succeeded)
        {
            // Most common cause: this Entra identity is already linked to another account.
            StatusMessage = "Error: this Microsoft Entra ID account is already linked to a user.";
            return RedirectToPage();
        }

        // Clear the external cookie left by the round-trip.
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);

        // Now that the identity is linked, reconcile directory roles onto the account
        // and refresh the cookie so any promotion takes effect immediately.
        if (await _roleSync.SyncAsync(user, info.Principal))
            await _signInManager.RefreshSignInAsync(user);

        StatusMessage = "Microsoft Entra ID has been linked. You can now sign in with it.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRemoveLoginAsync(string loginProvider, string providerKey)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return NotFound();

        var result = await _userManager.RemoveLoginAsync(user, loginProvider, providerKey);
        if (!result.Succeeded)
        {
            StatusMessage = "Error: the external login could not be removed.";
            return RedirectToPage();
        }

        await _signInManager.RefreshSignInAsync(user);
        StatusMessage = "The external login was removed.";
        return RedirectToPage();
    }

    private async Task LoadAsync(AppUser user)
    {
        CurrentLogins = await _userManager.GetLoginsAsync(user);
        EntraIdEnabled = AuthSetup.IsEntraIdEnabled(_config);
        EntraLinked = CurrentLogins.Any(l => l.LoginProvider == EntraProvider);

        var hasPassword = await _userManager.HasPasswordAsync(user);
        ShowRemoveButton = hasPassword || CurrentLogins.Count > 1;
    }
}
