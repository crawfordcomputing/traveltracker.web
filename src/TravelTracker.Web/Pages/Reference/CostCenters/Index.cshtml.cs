using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TravelTracker.Web.Data;
using TravelTracker.Web.Data.Entities;

namespace TravelTracker.Web.Pages.Reference.CostCenters;

// Manage the cost-center reference table. Authorized to Finance/Admin via the
// RequireCostCenters policy (Program.cs). Mirrors Admin/Departments but adds a
// Code + Name + active/inactive lifecycle.
public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    public IndexModel(AppDbContext db) => _db = db;

    public List<CostCenter> CostCenters { get; private set; } = new();

    [BindProperty, Required, StringLength(40), Display(Name = "Code")]
    public string NewCode { get; set; } = string.Empty;

    [BindProperty, StringLength(120), Display(Name = "Name")]
    public string? NewName { get; set; }

    public async Task OnGetAsync() => await LoadAsync();

    private async Task LoadAsync() =>
        CostCenters = await _db.CostCenters.OrderBy(c => c.Code).ToListAsync();

    public async Task<IActionResult> OnPostCreateAsync()
    {
        var code = NewCode?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(code))
            ModelState.AddModelError(nameof(NewCode), "Code is required.");
        else if (await _db.CostCenters.AnyAsync(c => c.Code == code))
            ModelState.AddModelError(nameof(NewCode), "A cost center with that code already exists.");

        if (!ModelState.IsValid)
        {
            await LoadAsync();
            return Page();
        }

        _db.CostCenters.Add(new CostCenter
        {
            Code = code,
            Name = string.IsNullOrWhiteSpace(NewName) ? null : NewName.Trim(),
            IsActive = true,
        });
        await _db.SaveChangesAsync();
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleAsync(int id)
    {
        var cc = await _db.CostCenters.FindAsync(id);
        if (cc is not null)
        {
            cc.IsActive = !cc.IsActive;
            await _db.SaveChangesAsync();
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var cc = await _db.CostCenters.FindAsync(id);
        if (cc is not null)
        {
            // Detach any references first so the FK (SetNull) never blocks the
            // delete on providers that don't apply it to untracked rows.
            var trips = await _db.Trips.Where(t => t.CostCenterId == id).ToListAsync();
            foreach (var t in trips) t.CostCenterId = null;
            var users = await _db.Users.Where(u => u.DefaultCostCenterId == id).ToListAsync();
            foreach (var u in users) u.DefaultCostCenterId = null;
            _db.CostCenters.Remove(cc);
            await _db.SaveChangesAsync();
        }
        return RedirectToPage();
    }
}
