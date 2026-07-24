using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TravelTracker.Web.Data.Entities;

namespace TravelTracker.Web.Pages.Account;

// Second factor after a correct password. PasswordSignInAsync returns RequiresTwoFactor
// and stashes the partial (TwoFactor) sign-in; this page completes it with a TOTP code.
[AllowAnonymous]
public class LoginWith2faModel : PageModel
{
    private readonly SignInManager<AppUser> _signInManager;

    public LoginWith2faModel(SignInManager<AppUser> signInManager)
        => _signInManager = signInManager;

    [BindProperty] public InputModel Input { get; set; } = new();
    public bool RememberMe { get; set; }
    public string? ReturnUrl { get; set; }

    public class InputModel
    {
        [Required, StringLength(8, MinimumLength = 6),
         Display(Name = "Authenticator code")]
        public string TwoFactorCode { get; set; } = string.Empty;

        [Display(Name = "Remember this machine")]
        public bool RememberMachine { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(bool rememberMe, string? returnUrl = null)
    {
        // No pending two-factor sign-in → nothing to complete; start over at Login.
        if (await _signInManager.GetTwoFactorAuthenticationUserAsync() is null)
            return RedirectToPage("./Login");

        ReturnUrl = returnUrl;
        RememberMe = rememberMe;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(bool rememberMe, string? returnUrl = null)
    {
        returnUrl ??= Url.Content("~/");
        RememberMe = rememberMe;
        ReturnUrl = returnUrl;
        if (!ModelState.IsValid) return Page();

        var user = await _signInManager.GetTwoFactorAuthenticationUserAsync();
        if (user is null) return RedirectToPage("./Login");

        var code = Regex.Replace(Input.TwoFactorCode, @"[\s-]", string.Empty);
        var result = await _signInManager.TwoFactorAuthenticatorSignInAsync(
            code, rememberMe, Input.RememberMachine);

        if (result.Succeeded) return LocalRedirect(returnUrl);

        if (result.IsLockedOut)
        {
            ModelState.AddModelError(string.Empty,
                "This account is temporarily locked due to repeated failed attempts. Try again later.");
            return Page();
        }

        ModelState.AddModelError(string.Empty, "Invalid authenticator code.");
        return Page();
    }
}
