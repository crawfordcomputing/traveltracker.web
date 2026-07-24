using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TravelTracker.Web.Data.Entities;
using TravelTracker.Web.Services;

namespace TravelTracker.Web.Pages.Account;

[AllowAnonymous]
public class ConfirmEmailModel : PageModel
{
    private readonly UserManager<AppUser> _userManager;

    public ConfirmEmailModel(UserManager<AppUser> userManager) => _userManager = userManager;

    public bool Confirmed { get; private set; }

    public async Task<IActionResult> OnGetAsync(string? userId, string? token)
    {
        // Missing/garbled link params: show the neutral failure state rather than 404.
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(token))
            return Page();

        var user = await _userManager.FindByIdAsync(userId);
        if (user is null) return Page();

        string raw;
        try { raw = EmailConfirmationService.DecodeToken(token); }
        catch { return Page(); }   // not valid base64url

        var result = await _userManager.ConfirmEmailAsync(user, raw);
        Confirmed = result.Succeeded;
        return Page();
    }
}
