using System.Security.Claims;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TravelTracker.Web.Data;
using TravelTracker.Web.Data.Entities;
using TravelTracker.Web.Domain;

namespace TravelTracker.Web.Pages.Mileage;

// Shared plumbing for the Mileage pages. Like Expenses, mileage entries have NO
// access rules of their own — they ride the parent trip's scope. Centralizing the
// load-then-authorize pair here (mirroring ExpensePageModel / TripPageModel) means
// no mileage page can silently skip the check.
public abstract class MileagePageModel : PageModel
{
    protected AppDbContext Db { get; }
    protected TeamAccess Team { get; }

    protected MileagePageModel(AppDbContext db, TeamAccess team)
    {
        Db = db;
        Team = team;
    }

    protected string? CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    // Loads a trip by id, returning it only if the current user may access it.
    protected async Task<Trip?> LoadAuthorizedTripAsync(int tripId)
    {
        var trip = await Db.Trips.FirstOrDefaultAsync(t => t.Id == tripId);
        if (trip is null) return null;

        var reachable = await Team.ReachableTravelerIdsAsync(User);
        return TripAccess.CanAccess(User, trip, reachable) ? trip : null;
    }

    // Loads a mileage entry (with its parent Trip and waypoints) only if the caller
    // may access that trip; null otherwise. The caller turns null into NotFound().
    protected async Task<MileageEntry?> LoadAuthorizedEntryAsync(int entryId)
    {
        var entry = await Db.MileageEntries
            .Include(m => m.Trip)
            .Include(m => m.Waypoints)
            .FirstOrDefaultAsync(m => m.Id == entryId);
        if (entry?.Trip is null) return null;

        var reachable = await Team.ReachableTravelerIdsAsync(User);
        return TripAccess.CanAccess(User, entry.Trip, reachable) ? entry : null;
    }

    // Resolves and freezes the rate for a (jurisdiction, vehicle, unit, date) tuple.
    // Returns null when no rate is in effect on that date, which the page surfaces as
    // a validation error pointing at the rate table.
    protected async Task<MileageRate?> ResolveRateAsync(
        string jurisdiction, VehicleType vehicle, DistanceUnit unit, DateOnly date)
    {
        var rates = await Db.MileageRates
            .Where(r => r.Jurisdiction == jurisdiction && r.VehicleType == vehicle && r.Unit == unit)
            .ToListAsync();
        return MileageRateResolver.Resolve(rates, jurisdiction, vehicle, unit, date);
    }
}
