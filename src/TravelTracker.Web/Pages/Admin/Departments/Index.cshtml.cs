using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using TravelTracker.Web.Data;
using TravelTracker.Web.Data.Entities;

namespace TravelTracker.Web.Pages.Admin.Departments;

public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly UserManager<AppUser> _userManager;

    public IndexModel(AppDbContext db, UserManager<AppUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    public List<Department> Departments { get; private set; } = new();

    // Active users offered as a department's default approver (value = Id,
    // text = "Display name (email)").
    public SelectList ApproverOptions { get; private set; } = default!;

    [BindProperty, Required, Display(Name = "New department")]
    public string NewName { get; set; } = string.Empty;

    public async Task OnGetAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        Departments = await _db.Departments
            .Include(d => d.DefaultApprover)
            .OrderBy(d => d.Name)
            .ToListAsync();
        ApproverOptions = await _db.ApproverSelectListAsync();
    }

    public async Task<IActionResult> OnPostCreateAsync()
    {
        var name = NewName?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(name))
            ModelState.AddModelError(nameof(NewName), "Name is required.");
        else if (await _db.Departments.AnyAsync(d => d.Name == name))
            ModelState.AddModelError(nameof(NewName), "A department with that name already exists.");

        if (!ModelState.IsValid)
        {
            await LoadAsync();
            return Page();
        }

        _db.Departments.Add(new Department { Name = name });
        await _db.SaveChangesAsync();
        return RedirectToPage();
    }

    // Set (or clear, when approverId is blank) a department's fallback approver.
    // Members with no personal ApproverId route here at submit time.
    public async Task<IActionResult> OnPostSetDefaultApproverAsync(int id, string? approverId)
    {
        var dept = await _db.Departments.FindAsync(id);
        if (dept is null) return NotFound();

        approverId = string.IsNullOrWhiteSpace(approverId) ? null : approverId;
        if (approverId is not null)
        {
            // Mirror the select list: only real, active users may be a default approver
            // (a forged post must not route trips to a deactivated account).
            var approver = await _userManager.FindByIdAsync(approverId);
            if (approver is null || !approver.IsActive)
            {
                TempData["Error"] = "Unknown or inactive approver.";
                return RedirectToPage();
            }
        }

        dept.DefaultApproverId = approverId;
        await _db.SaveChangesAsync();
        TempData["Status"] = approverId is null
            ? $"Cleared the default approver for {dept.Name}."
            : $"Updated the default approver for {dept.Name}.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var dept = await _db.Departments.FindAsync(id);
        if (dept is not null)
        {
            // Detach any users first so the FK (SetNull) doesn't block us.
            var members = await _db.Users.Where(u => u.DepartmentId == id).ToListAsync();
            foreach (var u in members) u.DepartmentId = null;
            _db.Departments.Remove(dept);
            await _db.SaveChangesAsync();
        }
        return RedirectToPage();
    }
}
