using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TravelTracker.Web.Data.Entities;
using TravelTracker.Web.Services;

namespace TravelTracker.Web.Pages.Account;

[AllowAnonymous]
public class ResendEmailConfirmationModel : PageModel
{
    private readonly UserManager<AppUser> _userManager;
    private readonly EmailConfirmationService _confirmations;

    public ResendEmailConfirmationModel(
        UserManager<AppUser> userManager, EmailConfirmationService confirmations)
    {
        _userManager = userManager;
        _confirmations = confirmations;
    }

    [BindProperty] public InputModel Input { get; set; } = new();

    // Same neutral outcome whether or not the account exists / is already confirmed,
    // so the form can't be used to enumerate registered or pending emails.
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

        // Only (re)send for an active account that hasn't confirmed yet.
        if (user is not null && user.IsActive && !user.EmailConfirmed)
        {
            await _confirmations.SendLinkAsync(user, (userId, token) =>
                Url.Page("/Account/ConfirmEmail", pageHandler: null,
                    values: new { userId, token }, protocol: Request.Scheme)!);
        }

        Submitted = true;
        return Page();
    }
}
