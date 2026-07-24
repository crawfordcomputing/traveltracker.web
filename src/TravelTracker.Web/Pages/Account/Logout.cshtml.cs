using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TravelTracker.Web.Data.Entities;

namespace TravelTracker.Web.Pages.Account;

[Authorize]
public class LogoutModel : PageModel
{
    private readonly SignInManager<AppUser> _signInManager;
    public LogoutModel(SignInManager<AppUser> signInManager) => _signInManager = signInManager;

    public async Task<IActionResult> OnPostAsync()
    {
        await _signInManager.SignOutAsync();
        return RedirectToPage("/Index");
    }

    public IActionResult OnGet() => RedirectToPage("/Index");
}
