using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TravelTracker.Web.Data;
using TravelTracker.Web.Data.Entities;
using TravelTracker.Web.Domain;

namespace TravelTracker.Web.Pages.Admin.Users;

public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly UserManager<AppUser> _userManager;

    public IndexModel(AppDbContext db, UserManager<AppUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    public record Row(string Id, string Email, string DisplayName, string? Department, string Roles, bool IsActive, string? Approver);
    public List<Row> Users { get; private set; } = new();

    public async Task OnGetAsync()
    {
        var users = await _db.Users
            .Include(u => u.Department)
            .Include(u => u.Approver)
            .OrderBy(u => u.Email).ToListAsync();

        foreach (var u in users)
        {
            var roles = await _userManager.GetRolesAsync(u);
            Users.Add(new Row(u.Id, u.Email ?? "", u.DisplayName,
                u.Department?.Name, string.Join(", ", roles), u.IsActive,
                u.Approver?.DisplayName));
        }
    }

    // Deactivate (offboard) or reactivate a user. Deactivating the last active
    // Admin is blocked so no one can lock everyone out. Bumping the security stamp
    // makes the deactivated user's live cookie fail its next revalidation.
    public async Task<IActionResult> OnPostToggleActiveAsync(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user is null) return NotFound();

        if (user.IsActive && await AdminGuard.IsLastActiveAdminAsync(_userManager, user))
        {
            TempData["Error"] = "You cannot deactivate the last active administrator.";
            return RedirectToPage();
        }

        user.IsActive = !user.IsActive;
        await _userManager.UpdateAsync(user);
        await _userManager.UpdateSecurityStampAsync(user);

        TempData["Status"] = user.IsActive
            ? $"{user.Email} reactivated."
            : $"{user.Email} deactivated — they can no longer sign in.";
        return RedirectToPage();
    }

    // Admin-initiated password reset: mint a strong one-time password and set it via
    // Identity's reset flow (which also rotates the security stamp, so the user's
    // existing sessions die at the next revalidation). The password is shown to the
    // admin exactly once via the same alert as new-user creation; it is never stored
    // in plaintext, so the admin must copy it now and share it securely.
    public async Task<IActionResult> OnPostResetPasswordAsync(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user is null) return NotFound();

        var password = TempPassword.Generate();
        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var result = await _userManager.ResetPasswordAsync(user, token, password);
        if (!result.Succeeded)
        {
            TempData["Error"] = "Could not reset the password: " +
                string.Join("; ", result.Errors.Select(e => e.Description));
            return RedirectToPage();
        }

        TempData["PasswordHeadline"] = "Password reset.";
        TempData["NewUserEmail"] = user.Email;
        TempData["NewUserPassword"] = password;
        return RedirectToPage();
    }
}
