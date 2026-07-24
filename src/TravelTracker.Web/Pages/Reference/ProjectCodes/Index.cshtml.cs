using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TravelTracker.Web.Data;
using TravelTracker.Web.Data.Entities;

namespace TravelTracker.Web.Pages.Reference.ProjectCodes;

// Manage the project-code reference table. Authorized to Finance/Manager/Admin
// via the RequireProjectCodes policy (Program.cs).
public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    public IndexModel(AppDbContext db) => _db = db;

    public List<ProjectCode> ProjectCodes { get; private set; } = new();

    [BindProperty, Required, StringLength(40), Display(Name = "Code")]
    public string NewCode { get; set; } = string.Empty;

    [BindProperty, StringLength(120), Display(Name = "Name")]
    public string? NewName { get; set; }

    public async Task OnGetAsync() => await LoadAsync();

    private async Task LoadAsync() =>
        ProjectCodes = await _db.ProjectCodes.OrderBy(p => p.Code).ToListAsync();

    public async Task<IActionResult> OnPostCreateAsync()
    {
        var code = NewCode?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(code))
            ModelState.AddModelError(nameof(NewCode), "Code is required.");
        else if (await _db.ProjectCodes.AnyAsync(p => p.Code == code))
            ModelState.AddModelError(nameof(NewCode), "A project code with that code already exists.");

        if (!ModelState.IsValid)
        {
            await LoadAsync();
            return Page();
        }

        _db.ProjectCodes.Add(new ProjectCode
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
        var pc = await _db.ProjectCodes.FindAsync(id);
        if (pc is not null)
        {
            pc.IsActive = !pc.IsActive;
            await _db.SaveChangesAsync();
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var pc = await _db.ProjectCodes.FindAsync(id);
        if (pc is not null)
        {
            // Detach any references first so the FK (SetNull) never blocks the
            // delete on providers that don't apply it to untracked rows.
            var trips = await _db.Trips.Where(t => t.ProjectCodeId == id).ToListAsync();
            foreach (var t in trips) t.ProjectCodeId = null;
            _db.ProjectCodes.Remove(pc);
            await _db.SaveChangesAsync();
        }
        return RedirectToPage();
    }
}
