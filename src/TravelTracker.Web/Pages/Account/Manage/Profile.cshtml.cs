using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TravelTracker.Web.Data;
using TravelTracker.Web.Data.Entities;
using TravelTracker.Web.Models;
using TravelTracker.Web.Services;

namespace TravelTracker.Web.Pages.Account.Manage;

// Self-service traveler profile. The /Account/Manage folder is authorized in
// Program.cs, so an anonymous request is redirected to Login. Travelers capture
// their own passport/KTN/preferences once here; admins can edit the same set from
// Admin/Users/Edit via the shared _TravelerProfileFields partial.
public class ProfileModel : PageModel
{
    private readonly UserManager<AppUser> _userManager;
    private readonly ISensitiveFieldProtector _protector;
    private readonly AppDbContext _db;

    public ProfileModel(UserManager<AppUser> userManager, ISensitiveFieldProtector protector, AppDbContext db)
    {
        _userManager = userManager;
        _protector = protector;
        _db = db;
    }

    [BindProperty] public TravelerProfileInput Input { get; set; } = new();
    [TempData] public string? StatusMessage { get; set; }
    public Microsoft.AspNetCore.Mvc.Rendering.SelectList CostCenterOptions { get; private set; } = default!;

    private async Task LoadListsAsync() =>
        CostCenterOptions = await _db.CostCenterSelectListAsync(Input.DefaultCostCenterId);

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return NotFound();

        Input = TravelerProfileInput.FromUser(user, _protector);
        await LoadListsAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return NotFound();

        if (Input.DefaultCostCenterId is int ccId && !await _db.CostCenters.AnyAsync(c => c.Id == ccId))
            ModelState.AddModelError("Input.DefaultCostCenterId", "Unknown cost center.");

        if (!ModelState.IsValid)
        {
            await LoadListsAsync();
            return Page();
        }

        Input.ApplyTo(user, _protector);
        await _userManager.UpdateAsync(user);

        StatusMessage = "Your profile has been saved.";
        return RedirectToPage();
    }
}
