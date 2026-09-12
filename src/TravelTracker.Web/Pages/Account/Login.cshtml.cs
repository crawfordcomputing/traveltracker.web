using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TravelTracker.Web.Data;
using TravelTracker.Web.Data.Entities;

namespace TravelTracker.Web.Pages.Account;

[AllowAnonymous]
public class LoginModel : PageModel
{
    private readonly SignInManager<AppUser> _signInManager;

    public LoginModel(SignInManager<AppUser> signInManager) => _signInManager = signInManager;

    [BindProperty] public InputModel Input { get; set; } = new();
    public string? ReturnUrl { get; set; }
    public bool EntraIdEnabled { get; set; }

    // Surfaced when an external (Entra) sign-in bounced back here; set via TempData
    // by ExternalLoginModel so the reason survives the redirect.
    [TempData] public string? ErrorMessage { get; set; }

    // Set when arriving from a completed password reset, to confirm success.
    public bool ResetSuccess { get; set; }

    // Set when a correct password was refused because the email isn't confirmed, so
    // the view can offer a resend link rather than a misleading "invalid" message.
    public bool NeedsEmailConfirmation { get; set; }

    public class InputModel
    {
        [Required, EmailAddress] public string Email { get; set; } = string.Empty;
        [Required, DataType(DataType.Password)] public string Password { get; set; } = string.Empty;
        [Display(Name = "Remember me")] public bool RememberMe { get; set; }
    }

    public void OnGet(string? returnUrl = null, bool resetSuccess = false)
    {
        ReturnUrl = returnUrl;
        ResetSuccess = resetSuccess;
        EntraIdEnabled = AuthSetup.IsEntraIdEnabled(
            HttpContext.RequestServices.GetRequiredService<IConfiguration>());
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        returnUrl ??= Url.Content("~/");
        if (!ModelState.IsValid) return Page();

        // lockoutOnFailure: true engages Identity's account lockout, throttling
        // password brute-force attempts.
        var result = await _signInManager.PasswordSignInAsync(
            Input.Email, Input.Password, Input.RememberMe, lockoutOnFailure: true);

        if (result.Succeeded) return LocalRedirect(returnUrl);

        // Password was correct but the account has 2FA on — collect the second factor.
        if (result.RequiresTwoFactor)
            return RedirectToPage("./LoginWith2fa",
                new { returnUrl, rememberMe = Input.RememberMe });

        if (result.IsLockedOut)
        {
            ModelState.AddModelError(string.Empty,
                "This account is temporarily locked due to repeated failed sign-in attempts. Try again later.");
            return Page();
        }

        // Correct password but the account can't sign in yet — with confirmation
        // required, that means the email is unconfirmed. Point them at the resend
        // page instead of the generic "invalid" error. (Deactivated accounts are
        // blocked earlier by AppSignInManager, so this is the confirmation case.)
        if (result.IsNotAllowed)
        {
            NeedsEmailConfirmation = true;
            ModelState.AddModelError(string.Empty,
                "You need to confirm your email address before signing in.");
            return Page();
        }

        ModelState.AddModelError(string.Empty, "Invalid email or password.");
        return Page();
    }
}
