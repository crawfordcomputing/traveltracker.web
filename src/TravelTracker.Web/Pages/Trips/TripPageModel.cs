using System.Security.Claims;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TravelTracker.Web.Data;
using TravelTracker.Web.Data.Entities;
using TravelTracker.Web.Domain;

namespace TravelTracker.Web.Pages.Trips;

// Shared plumbing for the Trips pages. The access *decision* is centralized in
// TripAccess (a pure function); this centralizes the *loading* so no page can
// silently skip the check. A new Trips page inherits this and calls
// LoadAuthorizedTripAsync instead of re-typing the load-then-authorize pair —
// removing that copy-paste closes the silent-authorization-hole risk.
public abstract class TripPageModel : PageModel
{
    protected AppDbContext Db { get; }
    protected TeamAccess Team { get; }

    protected TripPageModel(AppDbContext db, TeamAccess team)
    {
        Db = db;
        Team = team;
    }

    protected string? CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    // Loads a trip by id and returns it only if the current user may access it;
    // returns null otherwise (the caller turns that into NotFound()). `include`
    // shapes the query so each page eager-loads exactly what it needs
    // (Traveler / Destinations), e.g.
    //   await LoadAuthorizedTripAsync(id, q => q.Include(t => t.Destinations));
    protected async Task<Trip?> LoadAuthorizedTripAsync(
        int id, Func<IQueryable<Trip>, IQueryable<Trip>>? include = null)
    {
        var query = include is null ? Db.Trips : include(Db.Trips);
        var trip = await query.FirstOrDefaultAsync(t => t.Id == id);
        if (trip is null) return null;

        var reachable = await Team.ReachableTravelerIdsAsync(User);
        return TripAccess.CanAccess(User, trip, reachable) ? trip : null;
    }

    // The traveler-visibility scope for list/calendar queries (self + reachable
    // travelers, or everyone for an Admin). Filter with
    //   if (!scope.All) q = q.Where(t => scope.TravelerIds.Contains(t.TravelerId));
    protected async Task<TripScope> TravelerScopeAsync()
    {
        var reachable = await Team.ReachableTravelerIdsAsync(User);
        return TripAccess.Scope(User, reachable);
    }
}
