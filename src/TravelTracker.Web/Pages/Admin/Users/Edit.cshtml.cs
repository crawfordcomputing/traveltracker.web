using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using TravelTracker.Web.Data;
using TravelTracker.Web.Data.Entities;
using TravelTracker.Web.Domain;
using TravelTracker.Web.Models;
using TravelTracker.Web.Services;

namespace TravelTracker.Web.Pages.Admin.Users;

public class EditModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly UserManager<AppUser> _userManager;
    private readonly ISensitiveFieldProtector _protector;

    public EditModel(AppDbContext db, UserManager<AppUser> userManager, ISensitiveFieldProtector protector)
    {
        _db = db;
        _userManager = userManager;
        _protector = protector;
    }

    [BindProperty] public InputModel Input { get; set; } = new();
    [BindProperty] public TravelerProfileInput Profile { get; set; } = new();
    public string Email { get; private set; } = string.Empty;
    public SelectList Departments { get; private set; } = default!;
    public SelectList RoleOptions { get; private set; } = default!;
    public SelectList Approvers { get; private set; } = default!;
    public SelectList CostCenterOptions { get; private set; } = default!;
    public bool IsSelf { get; private set; }

    public class InputModel
    {
        [Required] public string Id { get; set; } = string.Empty;
        [Required, Display(Name = "Display name")] public string DisplayName { get; set; } = string.Empty;
        [Required] public string Role { get; set; } = Roles.Employee;
        [Display(Name = "Department")] public int? DepartmentId { get; set; }
        [Display(Name = "Approver")] public string? ApproverId { get; set; }
        [Display(Name = "Base location"), StringLength(120)] public string? BaseLocation { get; set; }
        [Display(Name = "Time zone"), StringLength(60)] public string? TimeZoneId { get; set; }
    }

    private async Task LoadLookupsAsync(AppUser user)
    {
        Email = user.Email ?? "";
        IsSelf = user.Id == _userManager.GetUserId(User);
        Departments = await _db.DepartmentSelectListAsync();
        RoleOptions = ReferenceLists.RoleSelectList();
        Approvers = await _db.ApproverSelectListAsync(excludeUserId: user.Id);
        CostCenterOptions = await _db.CostCenterSelectListAsync(Profile.DefaultCostCenterId);
    }

    public async Task<IActionResult> OnGetAsync(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user is null) return NotFound();

        var currentRole = (await _userManager.GetRolesAsync(user)).FirstOrDefault() ?? Roles.Employee;
        Input = new InputModel
        {
            Id = user.Id,
            DisplayName = user.DisplayName,
            Role = currentRole,
            DepartmentId = user.DepartmentId,
            ApproverId = user.ApproverId,
            BaseLocation = user.BaseLocation,
            TimeZoneId = user.TimeZoneId
        };
        Profile = TravelerProfileInput.FromUser(user, _protector);
        await LoadLookupsAsync(user);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var user = await _userManager.FindByIdAsync(Input.Id);
        if (user is null) return NotFound();

        // Separation of duties: a user may never change their own role, so a self-edit
        // can neither escalate nor demote. Pin the submitted role to the stored role
        // regardless of what was posted (defends against a forged/tampered field even
        // though the UI renders it read-only for self).
        if (user.Id == _userManager.GetUserId(User))
            Input.Role = (await _userManager.GetRolesAsync(user)).FirstOrDefault() ?? Roles.Employee;

        if (!Roles.All.Contains(Input.Role))
            ModelState.AddModelError("Input.Role", "Unknown role.");

        if (Profile.DefaultCostCenterId is int ccId && !await _db.CostCenters.AnyAsync(c => c.Id == ccId))
            ModelState.AddModelError("Profile.DefaultCostCenterId", "Unknown cost center.");

        // Approver: normalise "" -> null, then guard against a self- or cyclic
        // assignment so the M6 approval walk can never loop.
        var approverId = string.IsNullOrWhiteSpace(Input.ApproverId) ? null : Input.ApproverId;
        if (approverId is not null)
        {
            if (approverId == user.Id)
            {
                ModelState.AddModelError("Input.ApproverId", "A user cannot be their own approver.");
            }
            else if (await _userManager.FindByIdAsync(approverId) is null)
            {
                ModelState.AddModelError("Input.ApproverId", "Unknown approver.");
            }
            else
            {
                var chain = await _db.Users
                    .Where(u => u.ApproverId != null)
                    .Select(u => new { u.Id, u.ApproverId })
                    .ToDictionaryAsync(x => x.Id, x => x.ApproverId);
                if (ApproverGraph.CreatesCycle(user.Id, approverId, chain))
                    ModelState.AddModelError("Input.ApproverId",
                        "That approver would create a reporting cycle.");
            }
        }

        // Block a role change that would demote the last active administrator.
        var currentRoles = await _userManager.GetRolesAsync(user);
        if (currentRoles.Contains(Roles.Admin) && Input.Role != Roles.Admin
            && await AdminGuard.IsLastActiveAdminAsync(_userManager, user))
        {
            ModelState.AddModelError("Input.Role",
                "You cannot demote the last active administrator.");
        }

        if (!ModelState.IsValid)
        {
            await LoadLookupsAsync(user);
            return Page();
        }

        user.DisplayName = Input.DisplayName;
        user.DepartmentId = Input.DepartmentId;
        user.ApproverId = approverId;
        user.BaseLocation = string.IsNullOrWhiteSpace(Input.BaseLocation) ? null : Input.BaseLocation.Trim();
        user.TimeZoneId = string.IsNullOrWhiteSpace(Input.TimeZoneId) ? null : Input.TimeZoneId.Trim();
        Profile.ApplyTo(user, _protector);
        await _userManager.UpdateAsync(user);

        // Single-role model: replace whatever roles the user currently has. Skip the
        // churn (and security-stamp bump) when the role is unchanged, e.g. a self-edit.
        var existing = await _userManager.GetRolesAsync(user);
        if (!(existing.Count == 1 && existing[0] == Input.Role))
        {
            await _userManager.RemoveFromRolesAsync(user, existing);
            await _userManager.AddToRoleAsync(user, Input.Role);
        }

        return RedirectToPage("/Admin/Users/Index");
    }
}
