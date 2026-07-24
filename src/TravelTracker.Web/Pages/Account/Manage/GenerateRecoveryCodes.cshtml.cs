using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TravelTracker.Web.Data.Entities;

namespace TravelTracker.Web.Pages.Account.Manage;

// Regenerates the recovery-code set, invalidating any previous codes. Only meaningful
// once 2FA is on; otherwise we send the user to set it up first.
public class GenerateRecoveryCodesModel : PageModel
{
    private readonly UserManager<AppUser> _userManager;

    public GenerateRecoveryCodesModel(UserManager<AppUser> userManager)
        => _userManager = userManager;

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return NotFound();

        if (!await _userManager.GetTwoFactorEnabledAsync(user))
            return RedirectToPage("./TwoFactorAuthentication");

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return NotFound();

        if (!await _userManager.GetTwoFactorEnabledAsync(user))
            return RedirectToPage("./TwoFactorAuthentication");

        var codes = await _userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);
        TempData["RecoveryCodes"] = codes!.ToArray();
        return RedirectToPage("./ShowRecoveryCodes");
    }
}
