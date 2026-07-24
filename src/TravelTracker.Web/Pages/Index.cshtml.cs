using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TravelTracker.Web.Data;
using TravelTracker.Web.Data.Entities;
using TravelTracker.Web.Domain;

namespace TravelTracker.Web.Pages;

public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly TeamAccess _team;

    public IndexModel(AppDbContext db, TeamAccess team)
    {
        _db = db;
        _team = team;
    }

    public string DbProvider { get; private set; } = "SQL Server";
    public int DepartmentCount { get; private set; }
    public int UserCount { get; private set; }

    public bool ShowDashboard { get; private set; }
    public bool ScopeIsAll { get; private set; }
    // True when the dashboard spans travelers other than the viewer (Admin, or a
    // Manager/Arranger with a reachable team) — drives the traveler-name label.
    public bool ShowTraveler { get; private set; }
    public int DraftCount { get; private set; }
    public int PlannedCount { get; private set; }
    public int CompletedCount { get; private set; }
    public int UpcomingCount { get; private set; }
    public List<Trip> Upcoming { get; private set; } = new();

    public async Task OnGetAsync()
    {
        DepartmentCount = await _db.Departments.CountAsync();
        UserCount = await _db.Users.CountAsync();

        if (User.Identity?.IsAuthenticated != true) return;

        ShowDashboard = true;
        var reachable = await _team.ReachableTravelerIdsAsync(User);
        var scope = TripAccess.Scope(User, reachable);
        ScopeIsAll = scope.All;
        ShowTraveler = scope.All || reachable.Count > 0;

        var q = _db.Trips.Include(t => t.Traveler).AsQueryable();
        if (!scope.All)
            q = q.Where(t => scope.TravelerIds.Contains(t.TravelerId));

        var today = DateOnly.FromDateTime(DateTime.Today);
        DraftCount = await q.CountAsync(t => t.Status == TripStatus.Draft);
        PlannedCount = await q.CountAsync(t => t.Status == TripStatus.Planned);
        CompletedCount = await q.CountAsync(t => t.Status == TripStatus.Completed);

        var upcomingQ = q
            .Where(t => t.EndDate >= today
                && t.Status != TripStatus.Cancelled
                && t.Status != TripStatus.Completed)
            .OrderBy(t => t.StartDate);
        UpcomingCount = await upcomingQ.CountAsync();
        Upcoming = await upcomingQ.Take(5).ToListAsync();
    }
}
