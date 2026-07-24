using System.Security.Claims;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TravelTracker.Web.Data;
using TravelTracker.Web.Data.Entities;
using TravelTracker.Web.Domain;

namespace TravelTracker.Web.Pages.Reports;

// Shared plumbing for the /Reports pages. Centralizes the scope resolution and the
// EF projection into ReportRow so the pure ReportAggregation helper (and any export
// / chart page) all see the exact same numbers. The RequireReports policy on the
// folder keeps Employees out; ReportAccess decides the data scope for those allowed.
public abstract class ReportPageModel : PageModel
{
    protected AppDbContext Db { get; }
    protected TeamAccess Team { get; }

    protected ReportPageModel(AppDbContext db, TeamAccess team)
    {
        Db = db;
        Team = team;
    }

    protected string? CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    // Whether the signed-in user sees the whole org (Finance/Admin) vs a scoped slice.
    public bool IsOrgWide => ReportAccess.CanSeeOrgWide(User);

    // Loads the trips in the user's report scope, narrowed by the filter, and projects
    // each into a flat ReportRow (traveler, department, pre-summed expense & mileage
    // money, category breakdown). Cancelled trips are excluded — they incurred no
    // reportable spend and would distort counts. Expense splits are honoured the same
    // way ExpenseRollup does: split money is attributed to the split categories.
    //
    // Expenses and mileage are pulled in their own queries keyed by the in-scope trip
    // ids (Trip has no inverse navigation collections), then grouped in memory — keeps
    // this feature fully isolated from the entity model / schema.
    protected async Task<IReadOnlyList<ReportRow>> LoadRowsAsync(ReportFilter filter)
    {
        var reachable = await Team.ReachableTravelerIdsAsync(User);
        var scope = ReportAccess.Scope(User, reachable);

        var tripQuery = Db.Trips
            .AsNoTracking()
            .Include(t => t.Traveler).ThenInclude(u => u!.Department)
            .Include(t => t.CostCenter)
            .Where(t => t.Status != TripStatus.Cancelled);

        if (!scope.All)
            tripQuery = tripQuery.Where(t => scope.TravelerIds.Contains(t.TravelerId));
        if (filter.Status is { } st)
            tripQuery = tripQuery.Where(t => t.Status == st);
        if (filter.TravelerId is { } tid)
            tripQuery = tripQuery.Where(t => t.TravelerId == tid);

        var trips = (await tripQuery.ToListAsync())
            // Date range is an overlap test against the trip window — done in memory so
            // the semantics match ReportFilter.IncludesWindow exactly (and DateOnly
            // comparisons stay provider-agnostic across SQLite / Azure SQL).
            .Where(t => filter.IncludesWindow(t.StartDate, t.EndDate))
            .ToList();

        var tripIds = trips.Select(t => t.Id).ToList();
        if (tripIds.Count == 0) return Array.Empty<ReportRow>();

        var expensesByTrip = (await Db.Expenses
                .AsNoTracking()
                .Include(e => e.Splits)
                .Where(e => tripIds.Contains(e.TripId))
                .ToListAsync())
            .GroupBy(e => e.TripId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Expense>)g.ToList());

        var mileageByTrip = (await Db.MileageEntries
                .AsNoTracking()
                .Where(m => tripIds.Contains(m.TripId))
                .ToListAsync())
            .GroupBy(m => m.TripId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<MileageEntry>)g.ToList());

        return trips
            .Select(t => ToRow(
                t,
                expensesByTrip.GetValueOrDefault(t.Id) ?? Array.Empty<Expense>(),
                mileageByTrip.GetValueOrDefault(t.Id) ?? Array.Empty<MileageEntry>()))
            .ToList();
    }

    private static ReportRow ToRow(
        Trip t, IReadOnlyList<Expense> expenses, IReadOnlyList<MileageEntry> mileage)
    {
        var rollup = ExpenseRollup.Summarize(expenses);
        var mileageRollup = MileageRollup.Summarize(mileage);

        var categories = rollup.ByCategory
            .Select(c => new CategoryAmount(c.Category, c.Total))
            .ToList();

        return new ReportRow(
            TripId: t.Id,
            TravelerId: t.TravelerId,
            TravelerName: t.Traveler?.DisplayName ?? "—",
            DepartmentId: t.Traveler?.DepartmentId,
            DepartmentName: t.Traveler?.Department?.Name ?? "— none —",
            Status: t.Status,
            Start: t.StartDate,
            End: t.EndDate,
            CostCenter: t.CostCenter?.Code,
            Type: t.Type,
            ExpenseReimbursable: rollup.Reimbursable,
            ExpensePersonal: rollup.Personal,
            MileageAmount: mileageRollup.Amount,
            Categories: categories);
    }
}
