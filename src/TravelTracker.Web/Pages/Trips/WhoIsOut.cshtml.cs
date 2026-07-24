using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TravelTracker.Web.Data;
using TravelTracker.Web.Data.Entities;
using TravelTracker.Web.Domain;

namespace TravelTracker.Web.Pages.Trips;

[Authorize(Policy = "RequireManager")]
public class WhoIsOutModel : TripPageModel
{
    public WhoIsOutModel(AppDbContext db, TeamAccess team) : base(db, team) { }

    public record Row(string Traveler, string? BaseLocation, string Purpose, int TripId,
        DateOnly Start, DateOnly End, string? Where, DateOnly? PassportExpiry);

    public IReadOnlyList<Row> Rows { get; private set; } = Array.Empty<Row>();
    public string Range { get; private set; } = "today";
    public DateOnly WindowStart { get; private set; }
    public DateOnly WindowEnd { get; private set; }

    public async Task OnGetAsync(string? range)
    {
        Range = range == "week" ? "week" : "today";
        var today = DateOnly.FromDateTime(DateTime.Today);
        WindowStart = today;
        WindowEnd = Range == "week" ? today.AddDays(7) : today;

        // Admin sees the whole org; a Manager sees only their department (duty of care
        // for their team, not everyone). Reuses the same scoped-reach machinery as the
        // trip lists so the roster and the lists never disagree.
        var scope = await TravelerScopeAsync();

        var q = Db.Trips
            .Include(t => t.Traveler)
            .Include(t => t.Destinations)
            .Where(t => t.Status != TripStatus.Cancelled &&
                        t.StartDate <= WindowEnd && t.EndDate >= WindowStart);
        if (!scope.All)
            q = q.Where(t => scope.TravelerIds.Contains(t.TravelerId));

        var trips = await q
            .OrderBy(t => t.StartDate)
            .ToListAsync();

        Rows = trips.Select(t =>
        {
            // The leg covering the window start, else the first leg.
            var leg = t.Destinations.OrderBy(d => d.Sequence)
                          .FirstOrDefault(d => d.ArriveDate <= WindowEnd && d.DepartDate >= WindowStart)
                      ?? t.Destinations.OrderBy(d => d.Sequence).FirstOrDefault();
            var where = leg is null ? null
                : string.Join(", ", new[] { leg.City, leg.State, leg.Country }
                    .Where(s => !string.IsNullOrWhiteSpace(s)));
            return new Row(
                t.Traveler?.DisplayName ?? "—",
                t.Traveler?.BaseLocation,
                t.Purpose, t.Id, t.StartDate, t.EndDate, where,
                t.Traveler?.PassportExpiry);
        }).ToList();
    }
}
