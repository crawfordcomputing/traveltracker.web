using System.Globalization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TravelTracker.Web.Data;
using TravelTracker.Web.Data.Entities;
using TravelTracker.Web.Domain;

namespace TravelTracker.Web.Pages.Trips;

public class CalendarModel : TripPageModel
{
    public CalendarModel(AppDbContext db, TeamAccess team) : base(db, team) { }

    public int Year { get; private set; }
    public int Month { get; private set; }
    public string MonthName =>
        new DateTime(Year, Month, 1).ToString("MMMM yyyy", CultureInfo.CurrentCulture);
    public int DaysInMonth { get; private set; }
    public int LeadingBlanks { get; private set; } // Sunday-based offset before the 1st
    public List<Trip> Trips { get; private set; } = new();

    public (int y, int m) Prev => Month == 1 ? (Year - 1, 12) : (Year, Month - 1);
    public (int y, int m) Next => Month == 12 ? (Year + 1, 1) : (Year, Month + 1);

    public IEnumerable<Trip> TripsOn(DateOnly day) =>
        Trips.Where(t => t.StartDate <= day && t.EndDate >= day);

    public async Task OnGetAsync(int? year, int? month)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        Year = year ?? today.Year;
        Month = month ?? today.Month;
        if (Month < 1) { Month = 12; Year--; }
        if (Month > 12) { Month = 1; Year++; }

        var firstOfMonth = new DateOnly(Year, Month, 1);
        DaysInMonth = DateTime.DaysInMonth(Year, Month);
        LeadingBlanks = (int)firstOfMonth.DayOfWeek; // Sunday = 0

        var monthStart = firstOfMonth;
        var monthEnd = new DateOnly(Year, Month, DaysInMonth);

        var scope = await TravelerScopeAsync();
        var q = Db.Trips.Include(t => t.Traveler).AsQueryable();
        if (!scope.All)
            q = q.Where(t => scope.TravelerIds.Contains(t.TravelerId));

        Trips = await q
            .Where(t => t.StartDate <= monthEnd && t.EndDate >= monthStart)
            .OrderBy(t => t.StartDate)
            .ToListAsync();
    }
}
