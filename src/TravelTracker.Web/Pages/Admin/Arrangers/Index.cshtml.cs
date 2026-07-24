using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using TravelTracker.Web.Data;
using TravelTracker.Web.Data.Entities;

namespace TravelTracker.Web.Pages.Admin.Arrangers;

// Admin-only (inherits the /Admin RequireAdmin folder policy). Assigns travelers to a
// user holding the Arranger role: each assignment lets that arranger see and act for
// the traveler's trips. Managers/Admins keep global reach and don't appear here.
public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly UserManager<AppUser> _userManager;

    public IndexModel(AppDbContext db, UserManager<AppUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    public record ArrangerRow(string Id, string Email, string DisplayName, int Count);
    public record TravelerRow(int AssignmentId, string DisplayName, string? Email);

    public List<ArrangerRow> Arrangers { get; private set; } = new();
    public AppUser? Selected { get; private set; }
    public List<TravelerRow> Assigned { get; private set; } = new();
    public SelectList? AddCandidates { get; private set; }

    [BindProperty(SupportsGet = true)] public string? ArrangerId { get; set; }
    [BindProperty] public string? TravelerId { get; set; }

    public async Task OnGetAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        var arrangers = await _userManager.GetUsersInRoleAsync(Roles.Arranger);

        var counts = await _db.ArrangerAssignments
            .GroupBy(a => a.ArrangerId)
            .Select(g => new { ArrangerId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ArrangerId, x => x.Count);

        Arrangers = arrangers
            .OrderBy(u => u.DisplayName)
            .Select(u => new ArrangerRow(u.Id, u.Email ?? "", u.DisplayName,
                counts.TryGetValue(u.Id, out var c) ? c : 0))
            .ToList();

        if (string.IsNullOrEmpty(ArrangerId)) return;
        Selected = arrangers.FirstOrDefault(u => u.Id == ArrangerId);
        if (Selected is null) return;

        var assignments = await _db.ArrangerAssignments
            .Where(a => a.ArrangerId == ArrangerId)
            .Include(a => a.Traveler)
            .OrderBy(a => a.Traveler!.DisplayName)
            .ToListAsync();
        Assigned = assignments
            .Select(a => new TravelerRow(a.Id, a.Traveler?.DisplayName ?? "—", a.Traveler?.Email))
            .ToList();

        // Candidates = everyone not already assigned and not the arranger themselves
        // (an arranger always sees their own trips).
        var taken = assignments.Select(a => a.TravelerId).ToHashSet();
        taken.Add(ArrangerId);
        var candidates = await _db.Users
            .Where(u => !taken.Contains(u.Id))
            .OrderBy(u => u.DisplayName)
            .ToListAsync();
        AddCandidates = new SelectList(candidates, "Id", "DisplayName");
    }

    public async Task<IActionResult> OnPostAssignAsync()
    {
        if (string.IsNullOrEmpty(ArrangerId) || string.IsNullOrEmpty(TravelerId))
            return RedirectToPage(new { ArrangerId });

        var arranger = await _userManager.FindByIdAsync(ArrangerId);
        if (arranger is null || !await _userManager.IsInRoleAsync(arranger, Roles.Arranger))
        {
            TempData["Error"] = "That user is not an arranger.";
            return RedirectToPage(new { ArrangerId });
        }

        if (TravelerId == ArrangerId)
        {
            TempData["Error"] = "An arranger already has access to their own trips.";
            return RedirectToPage(new { ArrangerId });
        }

        if (await _db.Users.FindAsync(TravelerId) is null)
        {
            TempData["Error"] = "Unknown traveler.";
            return RedirectToPage(new { ArrangerId });
        }

        // Unique index also guards this; the check keeps a duplicate submit friendly.
        var exists = await _db.ArrangerAssignments
            .AnyAsync(a => a.ArrangerId == ArrangerId && a.TravelerId == TravelerId);
        if (!exists)
        {
            _db.ArrangerAssignments.Add(
                new ArrangerAssignment { ArrangerId = ArrangerId, TravelerId = TravelerId });
            await _db.SaveChangesAsync();
            TempData["Status"] = "Traveler assigned.";
        }
        return RedirectToPage(new { ArrangerId });
    }

    public async Task<IActionResult> OnPostUnassignAsync(int assignmentId)
    {
        var assignment = await _db.ArrangerAssignments.FindAsync(assignmentId);
        if (assignment is not null)
        {
            ArrangerId = assignment.ArrangerId;   // preserve selection after removal
            _db.ArrangerAssignments.Remove(assignment);
            await _db.SaveChangesAsync();
            TempData["Status"] = "Traveler unassigned.";
        }
        return RedirectToPage(new { ArrangerId });
    }
}
