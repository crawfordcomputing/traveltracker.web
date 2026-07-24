using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TravelTracker.Web.Data.Entities;

namespace TravelTracker.Web.Pages.Account;

// Fallback second factor: redeem a one-time recovery code when the authenticator app is
// unavailable. Redeeming a code consumes it.
[AllowAnonymous]
public class LoginWithRecoveryCodeModel : PageModel
{
    private readonly SignInManager<AppUser> _signInManager;

    public LoginWithRecoveryCodeModel(SignInManager<AppUser> signInManager)
        => _signInManager = signInManager;

    [BindProperty] public InputModel Input { get; set; } = new();
    public string? ReturnUrl { get; set; }

    public class InputModel
    {
        [Required, DataType(DataType.Text), Display(Name = "Recovery code")]
        public string RecoveryCode { get; set; } = string.Empty;
    }

    public async Task<IActionResult> OnGetAsync(string? returnUrl = null)
    {
        if (await _signInManager.GetTwoFactorAuthenticationUserAsync() is null)
            return RedirectToPage("./Login");

        ReturnUrl = returnUrl;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        returnUrl ??= Url.Content("~/");
        ReturnUrl = returnUrl;
        if (!ModelState.IsValid) return Page();

        var user = await _signInManager.GetTwoFactorAuthenticationUserAsync();
        if (user is null) return RedirectToPage("./Login");

        var code = Input.RecoveryCode.Replace(" ", string.Empty);
        var result = await _signInManager.TwoFactorRecoveryCodeSignInAsync(code);

        if (result.Succeeded) return LocalRedirect(returnUrl);

        if (result.IsLockedOut)
        {
            ModelState.AddModelError(string.Empty,
                "This account is temporarily locked due to repeated failed attempts. Try again later.");
            return Page();
        }

        ModelState.AddModelError(string.Empty, "Invalid recovery code.");
        return Page();
    }
}
