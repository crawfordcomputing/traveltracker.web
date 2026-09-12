using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using TravelTracker.Web.Data;
using TravelTracker.Web.Data.Entities;
using TravelTracker.Web.Services;

namespace TravelTracker.Web.Pages.Admin.Users;

public class CreateModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly UserManager<AppUser> _userManager;
    private readonly PasswordSetupMailer _setup;

    public CreateModel(AppDbContext db, UserManager<AppUser> userManager, PasswordSetupMailer setup)
    {
        _db = db;
        _userManager = userManager;
        _setup = setup;
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

        // Mirror the guard on Admin/Invites (which already refuses to invite an
        // existing user): creating the account here would strand the open invite as a
        // dead link, since redeeming it would hit the duplicate-email rule.
        if (await _db.Invitations.AnyAsync(i => i.Email == email && i.AcceptedAt == null))
            ModelState.AddModelError("Input.Email",
                "An open invite for that email already exists. Revoke it on Admin → Invites first, or let them accept it.");

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

        // Created with NO password: the account cannot be signed into until the user
        // follows the setup link and chooses one, so no credential the admin ever saw
        // can be used to sign in.
        var result = await _userManager.CreateAsync(user);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error.Description);
            await LoadLookupsAsync();
            return Page();
        }

        await _userManager.AddToRoleAsync(user, Input.Role);

        // Email the one-time setup link, and surface it on the next page too: the raw
        // token only exists here, and the admin may need to hand it over another way
        // when mail is down. Same pattern as invites.
        var token = await _setup.CreateTokenAsync(user);
        var link = Url.Page("/Account/ResetPassword", pageHandler: null,
            values: new { email, token }, protocol: Request.Scheme);
        var sent = await _setup.TrySendAccountSetupAsync(user, link!);

        TempData["SetupHeadline"] = "User created.";
        TempData["SetupEmail"] = email;
        TempData["SetupLink"] = link;
        TempData["SetupEmailed"] = sent;
        TempData["SetupExpiresIn"] = _setup.LifetimeText;
        return RedirectToPage("/Admin/Users/Index");
    }
}
