using System.Security.Claims;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TravelTracker.Web.Data;
using TravelTracker.Web.Data.Entities;
using TravelTracker.Web.Domain;

namespace TravelTracker.Web.Pages.Expenses;

// Shared plumbing for the Expenses pages. Expenses have NO access rules of their
// own — they ride the parent trip's scope. Centralizing the load-then-authorize
// pair here (as TripPageModel does for trips) means no expense page can silently
// skip the check.
public abstract class ExpensePageModel : PageModel
{
    protected AppDbContext Db { get; }
    protected TeamAccess Team { get; }

    protected ExpensePageModel(AppDbContext db, TeamAccess team)
    {
        Db = db;
        Team = team;
    }

    protected string? CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    // Loads a trip by id, returning it only if the current user may access it.
    protected async Task<Trip?> LoadAuthorizedTripAsync(
        int tripId, Func<IQueryable<Trip>, IQueryable<Trip>>? include = null)
    {
        var query = include is null ? Db.Trips : include(Db.Trips);
        var trip = await query.FirstOrDefaultAsync(t => t.Id == tripId);
        if (trip is null) return null;

        var reachable = await Team.ReachableTravelerIdsAsync(User);
        return TripAccess.CanAccess(User, trip, reachable) ? trip : null;
    }

    // Loads an expense (with its parent Trip) only if the caller may access that
    // trip; null otherwise. The caller turns null into NotFound().
    protected async Task<Expense?> LoadAuthorizedExpenseAsync(int expenseId)
    {
        var expense = await Db.Expenses
            .Include(e => e.Trip)
            .FirstOrDefaultAsync(e => e.Id == expenseId);
        if (expense?.Trip is null) return null;

        var reachable = await Team.ReachableTravelerIdsAsync(User);
        return TripAccess.CanAccess(User, expense.Trip, reachable) ? expense : null;
    }
}
