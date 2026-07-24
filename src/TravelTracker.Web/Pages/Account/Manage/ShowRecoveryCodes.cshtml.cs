using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace TravelTracker.Web.Pages.Account.Manage;

// Shows a freshly generated set of recovery codes exactly once. The codes are handed over
// via TempData by EnableAuthenticator / GenerateRecoveryCodes; a direct visit with none
// pending bounces back to the status page.
public class ShowRecoveryCodesModel : PageModel
{
    public string[] RecoveryCodes { get; set; } = System.Array.Empty<string>();

    public IActionResult OnGet()
    {
        if (TempData["RecoveryCodes"] is not string[] codes || codes.Length == 0)
            return RedirectToPage("./TwoFactorAuthentication");

        RecoveryCodes = codes;
        return Page();
    }
}
