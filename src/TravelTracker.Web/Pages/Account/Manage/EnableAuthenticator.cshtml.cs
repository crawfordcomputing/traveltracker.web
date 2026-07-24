using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TravelTracker.Web.Data.Entities;
using TravelTracker.Web.Domain;
using TravelTracker.Web.Services;

namespace TravelTracker.Web.Pages.Account.Manage;

// Enroll a TOTP authenticator: display the shared key (as a scannable QR + typed key),
// then verify a code from the app before switching 2FA on. On first enable we also mint a
// set of one-time recovery codes and show them once.
public class EnableAuthenticatorModel : PageModel
{
    private readonly UserManager<AppUser> _userManager;

    public EnableAuthenticatorModel(UserManager<AppUser> userManager)
        => _userManager = userManager;

    // Shown for manual entry; SharedKey is the grouped/lowercased form of the raw key.
    public string SharedKey { get; set; } = string.Empty;
    public string QrCodeDataUri { get; set; } = string.Empty;
    [BindProperty] public InputModel Input { get; set; } = new();

    public class InputModel
    {
        [Required, StringLength(8, MinimumLength = 6),
         Display(Name = "Verification code")]
        public string Code { get; set; } = string.Empty;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return NotFound();

        await LoadSharedKeyAsync(user);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return NotFound();

        if (!ModelState.IsValid)
        {
            await LoadSharedKeyAsync(user);
            return Page();
        }

        // Accept codes with spaces/dashes as pasted from the authenticator app.
        var code = Regex.Replace(Input.Code, @"[\s-]", string.Empty);
        var isValid = await _userManager.VerifyTwoFactorTokenAsync(
            user, _userManager.Options.Tokens.AuthenticatorTokenProvider, code);

        if (!isValid)
        {
            ModelState.AddModelError("Input.Code", "Verification code is invalid.");
            await LoadSharedKeyAsync(user);
            return Page();
        }

        await _userManager.SetTwoFactorEnabledAsync(user, true);

        // First-time enable with no existing codes: mint and show a set once.
        if (await _userManager.CountRecoveryCodesAsync(user) == 0)
        {
            var codes = await _userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);
            TempData["RecoveryCodes"] = codes!.ToArray();
            return RedirectToPage("./ShowRecoveryCodes");
        }

        TempData["StatusMessage"] = "Your authenticator app has been verified — 2FA is on.";
        return RedirectToPage("./TwoFactorAuthentication");
    }

    private async Task LoadSharedKeyAsync(AppUser user)
    {
        var key = await _userManager.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrEmpty(key))
        {
            await _userManager.ResetAuthenticatorKeyAsync(user);
            key = await _userManager.GetAuthenticatorKeyAsync(user);
        }

        SharedKey = AuthenticatorUri.FormatKey(key!);
        var email = await _userManager.GetEmailAsync(user) ?? user.UserName!;
        QrCodeDataUri = QrCodeGenerator.ToPngDataUri(AuthenticatorUri.Build(email, key!));
    }
}
