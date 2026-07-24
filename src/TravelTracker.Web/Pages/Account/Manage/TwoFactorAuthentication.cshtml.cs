using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TravelTracker.Web.Data.Entities;

namespace TravelTracker.Web.Pages.Account.Manage;

// Two-factor status hub. The /Account/Manage folder is authorized in Program.cs, so an
// anonymous request is redirected to Login.
public class TwoFactorAuthenticationModel : PageModel
{
    private readonly UserManager<AppUser> _userManager;

    public TwoFactorAuthenticationModel(UserManager<AppUser> userManager)
        => _userManager = userManager;

    public bool HasAuthenticator { get; set; }
    public bool Is2faEnabled { get; set; }
    public int RecoveryCodesLeft { get; set; }
    [TempData] public string? StatusMessage { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return NotFound();

        HasAuthenticator = await _userManager.GetAuthenticatorKeyAsync(user) is not null;
        Is2faEnabled = await _userManager.GetTwoFactorEnabledAsync(user);
        RecoveryCodesLeft = await _userManager.CountRecoveryCodesAsync(user);
        return Page();
    }
}
