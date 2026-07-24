using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using TravelTracker.Web.Data;
using TravelTracker.Web.Data.Entities;
using TravelTracker.Web.Domain;

namespace TravelTracker.Web.Pages.Admin.Users;

public class CreateModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly UserManager<AppUser> _userManager;

    public CreateModel(AppDbContext db, UserManager<AppUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    [BindProperty] public InputModel Input { get; set; } = new();
    public SelectList Departments { get; private set; } = default!;
    public SelectList RoleOptions { get; private set; } = default!;
    public SelectList Approvers { get; private set; } = default!;

    public class InputModel
    {
        [Required, Display(Name = "Display name")] public string DisplayName { get; set; } = string.Empty;
        [Required, EmailAddress] public string Email { get; set; } = string.Empty;
        [Required] public string Role { get; set; } = Roles.Employee;
        [Display(Name = "Department")] public int? DepartmentId { get; set; }
        [Display(Name = "Approver")] public string? ApproverId { get; set; }
        [Display(Name = "Base location"), StringLength(120)] public string? BaseLocation { get; set; }
        [Display(Name = "Time zone"), StringLength(60)] public string? TimeZoneId { get; set; }
    }

    private async Task LoadLookupsAsync()
    {
        Departments = await _db.DepartmentSelectListAsync();
        RoleOptions = ReferenceLists.RoleSelectList();
        Approvers = await _db.ApproverSelectListAsync();
    }

    public async Task<IActionResult> OnGetAsync()
    {
        await LoadLookupsAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!Roles.All.Contains(Input.Role))
            ModelState.AddModelError("Input.Role", "Unknown role.");

        var email = Input.Email.Trim();
        if (await _userManager.FindByEmailAsync(email) is not null)
            ModelState.AddModelError("Input.Email", "A user with that email already exists.");

        // A brand-new user is nobody's approver yet, so no cycle is possible; only
        // verify the chosen approver exists.
        var approverId = string.IsNullOrWhiteSpace(Input.ApproverId) ? null : Input.ApproverId;
        if (approverId is not null && await _userManager.FindByIdAsync(approverId) is null)
            ModelState.AddModelError("Input.ApproverId", "Unknown approver.");

        if (!ModelState.IsValid)
        {
            await LoadLookupsAsync();
            return Page();
        }

        var password = TempPassword.Generate();
        var user = new AppUser
        {
            UserName = email,
            Email = email,
            DisplayName = Input.DisplayName.Trim(),
            DepartmentId = Input.DepartmentId,
            ApproverId = approverId,
            BaseLocation = string.IsNullOrWhiteSpace(Input.BaseLocation) ? null : Input.BaseLocation.Trim(),
            TimeZoneId = string.IsNullOrWhiteSpace(Input.TimeZoneId) ? null : Input.TimeZoneId.Trim()
        };

        var result = await _userManager.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error.Description);
            await LoadLookupsAsync();
            return Page();
        }

        await _userManager.AddToRoleAsync(user, Input.Role);

        // Show the generated password exactly once on the next page. It is never
        // stored in plaintext, so this is the admin's only chance to copy it.
        TempData["PasswordHeadline"] = "User created.";
        TempData["NewUserEmail"] = email;
        TempData["NewUserPassword"] = password;
        return RedirectToPage("/Admin/Users/Index");
    }
}
