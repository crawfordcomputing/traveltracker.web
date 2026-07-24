using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TravelTracker.Web.Data;
using TravelTracker.Web.Data.Entities;

namespace TravelTracker.Web.Pages.Admin.Departments;

public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    public IndexModel(AppDbContext db) => _db = db;

    public List<Department> Departments { get; private set; } = new();

    [BindProperty, Required, Display(Name = "New department")]
    public string NewName { get; set; } = string.Empty;

    public async Task OnGetAsync() => await LoadAsync();

    private async Task LoadAsync() =>
        Departments = await _db.Departments.OrderBy(d => d.Name).ToListAsync();

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
