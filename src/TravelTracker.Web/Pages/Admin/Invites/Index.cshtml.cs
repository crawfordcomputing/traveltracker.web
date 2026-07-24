using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using TravelTracker.Web.Data;
using TravelTracker.Web.Data.Entities;
using TravelTracker.Web.Domain;

namespace TravelTracker.Web.Pages.Admin.Invites;

public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly UserManager<AppUser> _userManager;

    public IndexModel(AppDbContext db, UserManager<AppUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    [BindProperty] public InputModel Input { get; set; } = new();
    public SelectList Departments { get; private set; } = default!;
    public SelectList RoleOptions { get; private set; } = default!;

    public record Row(int Id, string Email, string Role, string? Department,
        DateTimeOffset ExpiresAt, DateTimeOffset? AcceptedAt, bool Expired);
    public List<Row> Invites { get; private set; } = new();

    public class InputModel
    {
        [Required, EmailAddress] public string Email { get; set; } = string.Empty;
        [Required] public string Role { get; set; } = Roles.Employee;
        [Display(Name = "Department")] public int? DepartmentId { get; set; }
        [Range(1, 60), Display(Name = "Valid for (days)")] public int ExpiresInDays { get; set; } = 7;
    }

    private async Task LoadAsync()
    {
        Departments = await _db.DepartmentSelectListAsync();
        RoleOptions = ReferenceLists.RoleSelectList();

        var now = DateTimeOffset.UtcNow;
        Invites = await _db.Invitations
            .Include(i => i.Department)
            .OrderByDescending(i => i.CreatedAt)
            .Select(i => new Row(i.Id, i.Email, i.Role,
                i.Department != null ? i.Department.Name : null,
                i.ExpiresAt, i.AcceptedAt, i.AcceptedAt == null && i.ExpiresAt <= now))
            .ToListAsync();
    }

    public async Task<IActionResult> OnGetAsync()
    {
        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostCreateAsync()
    {
        var email = Input.Email.Trim();

        if (!Roles.All.Contains(Input.Role))
            ModelState.AddModelError("Input.Role", "Unknown role.");
        if (await _userManager.FindByEmailAsync(email) is not null)
            ModelState.AddModelError("Input.Email", "A user with that email already exists.");
        if (await _db.Invitations.AnyAsync(i => i.Email == email && i.AcceptedAt == null))
            ModelState.AddModelError("Input.Email", "An open invite for that email already exists.");

        if (!ModelState.IsValid)
        {
            await LoadAsync();
            return Page();
        }

        var token = InviteTokens.NewToken();
        var invite = new Invitation
        {
            Email = email,
            Role = Input.Role,
            DepartmentId = Input.DepartmentId,
            TokenHash = InviteTokens.Hash(token),
            InvitedByUserId = _userManager.GetUserId(User) ?? string.Empty,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(Input.ExpiresInDays)
        };
        _db.Invitations.Add(invite);
        await _db.SaveChangesAsync();

        // The raw token exists only here — surface the link once for the admin to
        // copy. It's never recoverable after this render.
        var link = Url.Page("/Account/Register", pageHandler: null,
            values: new { token }, protocol: Request.Scheme);
        TempData["InviteEmail"] = email;
        TempData["InviteLink"] = link;
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRevokeAsync(int id)
    {
        var invite = await _db.Invitations.FindAsync(id);
        if (invite is not null && invite.AcceptedAt is null)
        {
            _db.Invitations.Remove(invite);
            await _db.SaveChangesAsync();
        }
        return RedirectToPage();
    }
}
