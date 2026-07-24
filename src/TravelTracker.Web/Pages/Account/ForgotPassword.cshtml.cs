using System.ComponentModel.DataAnnotations;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using TravelTracker.Web.Data.Entities;
using TravelTracker.Web.Services;

namespace TravelTracker.Web.Pages.Account;

[AllowAnonymous]
public class ForgotPasswordModel : PageModel
{
    private readonly UserManager<AppUser> _userManager;
    private readonly IEmailSender _email;

    public ForgotPasswordModel(UserManager<AppUser> userManager, IEmailSender email)
    {
        _userManager = userManager;
        _email = email;
    }

    [BindProperty] public InputModel Input { get; set; } = new();

    // Shown after any submit — deliberately the same message whether or not the
    // account exists, so the form can't be used to enumerate registered emails.
    public bool Submitted { get; private set; }

    public class InputModel
    {
        [Required, EmailAddress] public string Email { get; set; } = string.Empty;
    }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        var user = await _userManager.FindByEmailAsync(Input.Email);

        // Only send a mail when the account exists and can still sign in (a
        // deactivated leaver gets no reset link). Either way the UI reports the same
        // neutral outcome below, so the form can't confirm which emails are real.
        if (user is not null && user.IsActive)
        {
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var encoded = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
            var link = Url.Page("/Account/ResetPassword", pageHandler: null,
                values: new { email = Input.Email, token = encoded }, protocol: Request.Scheme);

            await _email.SendAsync(Input.Email, "Reset your Travel Tracker password",
                $"<p>Someone requested a password reset for your account.</p>" +
                $"<p><a href=\"{link}\">Reset your password</a></p>" +
                $"<p>If this wasn't you, you can safely ignore this email.</p>");
        }

        Submitted = true;
        return Page();
    }
}
