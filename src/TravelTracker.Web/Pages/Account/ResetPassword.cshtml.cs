using System.ComponentModel.DataAnnotations;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using TravelTracker.Web.Data.Entities;

namespace TravelTracker.Web.Pages.Account;

[AllowAnonymous]
public class ResetPasswordModel : PageModel
{
    private readonly UserManager<AppUser> _userManager;

    public ResetPasswordModel(UserManager<AppUser> userManager) => _userManager = userManager;

    [BindProperty] public InputModel Input { get; set; } = new();

    public class InputModel
    {
        [Required, EmailAddress] public string Email { get; set; } = string.Empty;

        // The base64url-encoded reset token carried by the emailed link.
        [Required] public string Token { get; set; } = string.Empty;

        [Required, StringLength(100, MinimumLength = 8), DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [DataType(DataType.Password), Display(Name = "Confirm password"),
         Compare(nameof(Password), ErrorMessage = "Passwords do not match.")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    public IActionResult OnGet(string? email = null, string? token = null)
    {
        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(token))
        {
            // No link params — nothing to reset. Send them to request a fresh link.
            return RedirectToPage("/Account/ForgotPassword");
        }

        Input.Email = email;
        Input.Token = token;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        var user = await _userManager.FindByEmailAsync(Input.Email);
        if (user is null)
        {
            // Don't reveal that the account is missing — same neutral outcome as a
            // bad/expired token below.
            return RedirectToPage("/Account/Login", new { resetSuccess = true });
        }

        string token;
        try
        {
            token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(Input.Token));
        }
        catch (FormatException)
        {
            ModelState.AddModelError(string.Empty, "This reset link is invalid. Request a new one.");
            return Page();
        }

        var result = await _userManager.ResetPasswordAsync(user, token, Input.Password);
        if (result.Succeeded)
            return RedirectToPage("/Account/Login", new { resetSuccess = true });

        foreach (var error in result.Errors)
            ModelState.AddModelError(string.Empty, error.Description);
        return Page();
    }
}
