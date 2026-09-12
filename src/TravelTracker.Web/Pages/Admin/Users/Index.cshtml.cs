using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using TravelTracker.Web.Data;
using TravelTracker.Web.Data.Entities;
using TravelTracker.Web.Domain;
using TravelTracker.Web.Services;

namespace TravelTracker.Web.Pages.Admin.Users;

public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly UserManager<AppUser> _userManager;
    private readonly PasswordSetupMailer _setup;

    public IndexModel(AppDbContext db, UserManager<AppUser> userManager, PasswordSetupMailer setup)
    {
        _db = db;
        _userManager = userManager;
        _setup = setup;
    }

    public record Row(string Id, string Email, string DisplayName, string? Department, string Roles, bool IsActive, string? Approver, string? DeptDefaultApprover);
    public List<Row> Users { get; private set; } = new();

    // Active users offered as the bulk-assign target (value = Id, text = "name (email)").
    public SelectList ApproverOptions { get; private set; } = default!;

    // Filter pickers, populated on every GET so the form can round-trip.
    public SelectList DepartmentOptions { get; private set; } = default!;
    public SelectList RoleOptions { get; private set; } = default!;

    // Current filters/sort, echoed back so the UI can reflect and preserve them.
    // The directory can run to hundreds of accounts, so narrowing it is the normal
    // way in rather than a nicety.
    [BindProperty(SupportsGet = true)] public string? Search { get; set; }       // free text over display name + email
    [BindProperty(SupportsGet = true)] public string? Status { get; set; }       // null|all, active, inactive
    [BindProperty(SupportsGet = true)] public int? DepartmentId { get; set; }
    [BindProperty(SupportsGet = true)] public string? Role { get; set; }
    [BindProperty(SupportsGet = true)] public string? Filter { get; set; }   // null|all, unassigned, assigned
    [BindProperty(SupportsGet = true)] public string? Sort { get; set; }     // email[_desc], name, department, approver, status

    // True when anything is narrowing the list — drives the "Clear filters" affordance.
    public bool HasFilters =>
        !string.IsNullOrWhiteSpace(Search) || !string.IsNullOrWhiteSpace(Status)
        || DepartmentId is not null || !string.IsNullOrWhiteSpace(Role)
        || !string.IsNullOrWhiteSpace(Filter);

    // The current view as route values, so sort links and post-action redirects
    // land the admin back on the same filtered page. Null entries drop out of the
    // generated URL. Pass `sort` to override just the sort key.
    public IDictionary<string, object?> FilterRoute(string? sort = null) => new Dictionary<string, object?>
    {
        ["Search"] = string.IsNullOrWhiteSpace(Search) ? null : Search,
        ["Status"] = string.IsNullOrWhiteSpace(Status) ? null : Status,
        ["DepartmentId"] = DepartmentId,
        ["Role"] = string.IsNullOrWhiteSpace(Role) ? null : Role,
        ["Filter"] = string.IsNullOrWhiteSpace(Filter) ? null : Filter,
        ["Sort"] = string.IsNullOrWhiteSpace(sort ?? Sort) ? null : sort ?? Sort,
    };

    // Free-text search is user-supplied, so its LIKE metacharacters have to be
    // neutered or typing "100%" would match everything. Paired with the ESCAPE
    // clause that EF.Functions.Like's escapeChar overload emits.
    private const string LikeEscape = "\\";
    private static string EscapeLike(string term) => term
        .Replace(LikeEscape, LikeEscape + LikeEscape)
        .Replace("%", LikeEscape + "%")
        .Replace("_", LikeEscape + "_")
        .Replace("[", LikeEscape + "[");

    public async Task OnGetAsync()
    {
        IQueryable<AppUser> q = _db.Users
            .Include(u => u.Department).ThenInclude(d => d!.DefaultApprover)
            .Include(u => u.Approver);

        if (!string.IsNullOrWhiteSpace(Search))
        {
            var pattern = $"%{EscapeLike(Search.Trim())}%";
            q = q.Where(u =>
                EF.Functions.Like(u.DisplayName, pattern, LikeEscape) ||
                EF.Functions.Like(u.Email!, pattern, LikeEscape));
        }

        q = Status switch
        {
            "active"   => q.Where(u => u.IsActive),
            "inactive" => q.Where(u => !u.IsActive),
            _          => q,
        };

        if (DepartmentId is int deptId)
            q = q.Where(u => u.DepartmentId == deptId);

        if (!string.IsNullOrWhiteSpace(Role))
        {
            // Role membership lives in Identity's join table, so narrow by an IN
            // subquery rather than pulling every user's roles back to compare here.
            var role = Role;
            var inRole = _db.UserRoles
                .Where(ur => _db.Roles.Any(r => r.Id == ur.RoleId && r.Name == role))
                .Select(ur => ur.UserId);
            q = q.Where(u => inRole.Contains(u.Id));
        }

        q = Filter switch
        {
            "unassigned" => q.Where(u => u.ApproverId == null),
            "assigned"   => q.Where(u => u.ApproverId != null),
            _            => q,
        };

        q = Sort switch
        {
            "email_desc"      => q.OrderByDescending(u => u.Email),
            "name"            => q.OrderBy(u => u.DisplayName),
            "name_desc"       => q.OrderByDescending(u => u.DisplayName),
            "department"      => q.OrderBy(u => u.Department!.Name).ThenBy(u => u.Email),
            "department_desc" => q.OrderByDescending(u => u.Department!.Name).ThenBy(u => u.Email),
            "approver"        => q.OrderBy(u => u.Approver!.DisplayName).ThenBy(u => u.Email),
            "approver_desc"   => q.OrderByDescending(u => u.Approver!.DisplayName).ThenBy(u => u.Email),
            "status"          => q.OrderByDescending(u => u.IsActive).ThenBy(u => u.Email),
            "status_desc"     => q.OrderBy(u => u.IsActive).ThenBy(u => u.Email),
            _                 => q.OrderBy(u => u.Email),
        };

        var users = await q.ToListAsync();

        // One query for every user's roles instead of GetRolesAsync per row (N+1).
        var rolesByUser = (await _db.UserRoles
                .Join(_db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { ur.UserId, r.Name })
                .ToListAsync())
            .GroupBy(x => x.UserId)
            .ToDictionary(g => g.Key, g => string.Join(", ", g.Select(x => x.Name).OrderBy(n => n)));

        foreach (var u in users)
        {
            var deptDefault = u.ApproverId == null ? u.Department?.DefaultApprover?.DisplayName : null;
            Users.Add(new Row(u.Id, u.Email ?? "", u.DisplayName,
                u.Department?.Name, rolesByUser.GetValueOrDefault(u.Id, ""), u.IsActive,
                u.Approver?.DisplayName, deptDefault));
        }

        ApproverOptions = await _db.ApproverSelectListAsync();
        DepartmentOptions = await _db.DepartmentSelectListAsync(DepartmentId);
        RoleOptions = ReferenceLists.RoleSelectList(Role);
    }

    // Assign one approver to many users in a single action. Rows that would make a
    // user their own approver, or that would create a reporting cycle, are skipped
    // and reported; the rest are applied. See Domain/ApproverGraph.
    public async Task<IActionResult> OnPostBulkAssignAsync(string[] selectedIds, string? bulkApproverId)
    {
        bulkApproverId = string.IsNullOrWhiteSpace(bulkApproverId) ? null : bulkApproverId;

        if (selectedIds is null || selectedIds.Length == 0)
        {
            TempData["Error"] = "Select at least one user first.";
            return RedirectToPage(FilterRoute());
        }
        if (bulkApproverId is null)
        {
            TempData["Error"] = "Choose an approver to assign.";
            return RedirectToPage(FilterRoute());
        }
        // Mirror the select list: only real, active users may be assigned as an
        // approver (a forged post must not route trips to a deactivated account).
        var approver = await _userManager.FindByIdAsync(bulkApproverId);
        if (approver is null || !approver.IsActive)
        {
            TempData["Error"] = "Unknown or inactive approver.";
            return RedirectToPage(FilterRoute());
        }

        // The current approver map (userId -> approverId), fed to the pure planner
        // which decides assign-vs-skip and mutates the map so in-batch chains are
        // caught. Only distinct, real user ids are considered.
        var chain = await _db.Users
            .Where(u => u.ApproverId != null)
            .Select(u => new { u.Id, u.ApproverId })
            .ToDictionaryAsync(x => x.Id, x => x.ApproverId);

        var selected = await _db.Users
            .Where(u => selectedIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id);

        var (toAssign, skipped) = ApproverGraph.PlanBulkApproverAssignment(
            bulkApproverId, selected.Keys, chain);

        foreach (var uid in toAssign)
            selected[uid].ApproverId = bulkApproverId;
        if (toAssign.Count > 0) await _db.SaveChangesAsync();

        var approverName = approver.DisplayName;
        var msg = $"Assigned {approverName} to {toAssign.Count} user{(toAssign.Count == 1 ? "" : "s")}.";
        if (skipped.Count > 0)
        {
            var details = skipped.Select(s =>
            {
                var email = selected.TryGetValue(s.UserId, out var u) ? u.Email : s.UserId;
                var reason = s.Reason == ApproverGraph.BulkSkipReason.SelfApproval
                    ? "can't approve their own trips"
                    : "would create a reporting cycle";
                return $"{email} ({reason})";
            });
            msg += $" Skipped {skipped.Count}: {string.Join("; ", details)}.";
        }
        TempData["Status"] = msg;

        return RedirectToPage(FilterRoute());
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
            return RedirectToPage(FilterRoute());
        }

        user.IsActive = !user.IsActive;
        await _userManager.UpdateAsync(user);
        await _userManager.UpdateSecurityStampAsync(user);

        TempData["Status"] = user.IsActive
            ? $"{user.Email} reactivated."
            : $"{user.Email} deactivated — they can no longer sign in.";
        return RedirectToPage(FilterRoute());
    }

    // Admin-initiated password reset: email the user a one-time link to choose their
    // own password, and surface it for the admin as a fallback. No password is ever
    // generated or handed over, so nothing an admin saw can be replayed later.
    //
    // The user's current password keeps working until they set a new one (same
    // semantics as self-service "Forgot password"), so a bounced email can't lock
    // anyone out. To cut off a compromised account now, deactivate it — that bumps
    // the security stamp and kills live sessions.
    public async Task<IActionResult> OnPostResetPasswordAsync(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user is null) return NotFound();

        var token = await _setup.CreateTokenAsync(user);
        var link = Url.Page("/Account/ResetPassword", pageHandler: null,
            values: new { email = user.Email, token }, protocol: Request.Scheme);
        var sent = await _setup.TrySendPasswordResetAsync(user, link!);

        TempData["SetupHeadline"] = "Password reset link issued.";
        TempData["SetupEmail"] = user.Email;
        TempData["SetupLink"] = link;
        TempData["SetupEmailed"] = sent;
        TempData["SetupExpiresIn"] = _setup.LifetimeText;
        return RedirectToPage(FilterRoute());
    }
}
