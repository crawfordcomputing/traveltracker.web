using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using TravelTracker.Web.Data;
using TravelTracker.Web.Data.Entities;
using TravelTracker.Web.Domain;

namespace TravelTracker.Web.Pages.Admin.Users;

public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly UserManager<AppUser> _userManager;

    public IndexModel(AppDbContext db, UserManager<AppUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    public record Row(string Id, string Email, string DisplayName, string? Department, string Roles, bool IsActive, string? Approver, string? DeptDefaultApprover);
    public List<Row> Users { get; private set; } = new();

    // Active users offered as the bulk-assign target (value = Id, text = "name (email)").
    public SelectList ApproverOptions { get; private set; } = default!;

    // Current filter/sort, echoed back so the UI can reflect and preserve them.
    [BindProperty(SupportsGet = true)] public string? Filter { get; set; }   // null|all, unassigned, assigned
    [BindProperty(SupportsGet = true)] public string? Sort { get; set; }     // email[_desc], name, department, approver, status

    public async Task OnGetAsync()
    {
        IQueryable<AppUser> q = _db.Users
            .Include(u => u.Department).ThenInclude(d => d!.DefaultApprover)
            .Include(u => u.Approver);

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
        foreach (var u in users)
        {
            var roles = await _userManager.GetRolesAsync(u);
            var deptDefault = u.ApproverId == null ? u.Department?.DefaultApprover?.DisplayName : null;
            Users.Add(new Row(u.Id, u.Email ?? "", u.DisplayName,
                u.Department?.Name, string.Join(", ", roles), u.IsActive,
                u.Approver?.DisplayName, deptDefault));
        }

        ApproverOptions = await _db.ApproverSelectListAsync();
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
            return RedirectToPage(new { Filter, Sort });
        }
        if (bulkApproverId is null)
        {
            TempData["Error"] = "Choose an approver to assign.";
            return RedirectToPage(new { Filter, Sort });
        }
        // Mirror the select list: only real, active users may be assigned as an
        // approver (a forged post must not route trips to a deactivated account).
        var approver = await _userManager.FindByIdAsync(bulkApproverId);
        if (approver is null || !approver.IsActive)
        {
            TempData["Error"] = "Unknown or inactive approver.";
            return RedirectToPage(new { Filter, Sort });
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

        return RedirectToPage(new { Filter, Sort });
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
            return RedirectToPage();
        }

        user.IsActive = !user.IsActive;
        await _userManager.UpdateAsync(user);
        await _userManager.UpdateSecurityStampAsync(user);

        TempData["Status"] = user.IsActive
            ? $"{user.Email} reactivated."
            : $"{user.Email} deactivated — they can no longer sign in.";
        return RedirectToPage();
    }

    // Admin-initiated password reset: mint a strong one-time password and set it via
    // Identity's reset flow (which also rotates the security stamp, so the user's
    // existing sessions die at the next revalidation). The password is shown to the
    // admin exactly once via the same alert as new-user creation; it is never stored
    // in plaintext, so the admin must copy it now and share it securely.
    public async Task<IActionResult> OnPostResetPasswordAsync(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user is null) return NotFound();

        var password = TempPassword.Generate();
        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var result = await _userManager.ResetPasswordAsync(user, token, password);
        if (!result.Succeeded)
        {
            TempData["Error"] = "Could not reset the password: " +
                string.Join("; ", result.Errors.Select(e => e.Description));
            return RedirectToPage();
        }

        TempData["PasswordHeadline"] = "Password reset.";
        TempData["NewUserEmail"] = user.Email;
        TempData["NewUserPassword"] = password;
        return RedirectToPage();
    }
}
