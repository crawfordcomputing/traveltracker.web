using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TravelTracker.Web.Data.Entities;

namespace TravelTracker.Web.Pages.Account.Manage;

// Signed-in self-service password change. The /Account/Manage folder is authorized
// in Program.cs, so an anonymous request is redirected to Login.
public class ChangePasswordModel : PageModel
{
    private readonly UserManager<AppUser> _userManager;
    private readonly SignInManager<AppUser> _signInManager;

    public ChangePasswordModel(
        UserManager<AppUser> userManager, SignInManager<AppUser> signInManager)
    {
        _userManager = userManager;
        _signInManager = signInManager;
    }

    [BindProperty] public InputModel Input { get; set; } = new();
    [TempData] public string? StatusMessage { get; set; }

    public class InputModel
    {
        [Required, DataType(DataType.Password), Display(Name = "Current password")]
        public string CurrentPassword { get; set; } = string.Empty;

        [Required, StringLength(100, MinimumLength = 8), DataType(DataType.Password),
         Display(Name = "New password")]
        public string NewPassword { get; set; } = string.Empty;

        [DataType(DataType.Password), Display(Name = "Confirm new password"),
         Compare(nameof(NewPassword), ErrorMessage = "Passwords do not match.")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    public IActionResult OnGet() => Page();

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        var user = await _userManager.GetUserAsync(User);
        if (user is null) return NotFound();

        var result = await _userManager.ChangePasswordAsync(
            user, Input.CurrentPassword, Input.NewPassword);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error.Description);
            return Page();
        }

        // Refresh the sign-in cookie so the rotated security stamp doesn't bounce
        // this session out at the next revalidation.
        await _signInManager.RefreshSignInAsync(user);
        StatusMessage = "Your password has been changed.";
        return RedirectToPage();
    }
}
