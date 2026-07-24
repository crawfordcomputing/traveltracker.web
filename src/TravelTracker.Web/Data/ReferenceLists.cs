using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using TravelTracker.Web.Data.Entities;

namespace TravelTracker.Web.Data;

// Shared builders for the reference-data <select> lists that several admin pages
// (Users/Create, Users/Edit, Invites/Index) all populate the same way. Keeping
// them in one place means the ordering and value/text fields can't drift between
// pages.
public static class ReferenceLists
{
    // Departments ordered by name (value = Id, text = Name).
    public static async Task<SelectList> DepartmentSelectListAsync(
        this AppDbContext db, object? selected = null) =>
        new SelectList(
            await db.Departments.OrderBy(d => d.Name).ToListAsync(),
            "Id", "Name", selected);

    // The assignable role names.
    public static SelectList RoleSelectList(object? selected = null) =>
        new SelectList(Roles.All, selected);

    // Cost centers for a picker: active ones ordered by Code, plus the currently
    // selected one even if it's since been deactivated (so an existing trip's link
    // stays visible/selectable). Text = "Code — Name" (or just Code). value = Id.
    public static async Task<SelectList> CostCenterSelectListAsync(
        this AppDbContext db, int? selectedId = null)
    {
        // Filter/order on scalar columns then project to a scalar shape so EF can
        // translate the whole query; label formatting happens in memory below.
        var rows = await db.CostCenters
            .Where(c => c.IsActive || (selectedId != null && c.Id == selectedId))
            .OrderBy(c => c.Code)
            .Select(c => new CodeRow(c.Id, c.Code, c.Name))
            .ToListAsync();
        return BuildCodeSelectList(rows, selectedId);
    }

    // Project codes for a picker; same active-plus-selected rule as cost centers.
    public static async Task<SelectList> ProjectCodeSelectListAsync(
        this AppDbContext db, int? selectedId = null)
    {
        var rows = await db.ProjectCodes
            .Where(p => p.IsActive || (selectedId != null && p.Id == selectedId))
            .OrderBy(p => p.Code)
            .Select(p => new CodeRow(p.Id, p.Code, p.Name))
            .ToListAsync();
        return BuildCodeSelectList(rows, selectedId);
    }

    private sealed record CodeRow(int Id, string Code, string? Name);

    private static SelectList BuildCodeSelectList(IEnumerable<CodeRow> rows, int? selectedId)
    {
        var items = rows.Select(r => new
        {
            r.Id,
            Label = string.IsNullOrWhiteSpace(r.Name) ? r.Code : r.Code + " — " + r.Name,
        });
        return new SelectList(items, "Id", "Label", selectedId);
    }

    // Candidate approvers (value = user Id, text = "Display name (email)"), ordered
    // by display name. Optionally excludes one user (so a user can't be offered as
    // their own approver on the Edit page) and inactive accounts (an offboarded user
    // shouldn't be assignable as a new approver, though existing links are kept).
    public static async Task<SelectList> ApproverSelectListAsync(
        this AppDbContext db, string? excludeUserId = null, object? selected = null)
    {
        var users = await db.Users
            .Where(u => u.IsActive && (excludeUserId == null || u.Id != excludeUserId))
            .OrderBy(u => u.DisplayName)
            .Select(u => new { u.Id, Label = u.DisplayName + " (" + u.Email + ")" })
            .ToListAsync();
        return new SelectList(users, "Id", "Label", selected);
    }
}
