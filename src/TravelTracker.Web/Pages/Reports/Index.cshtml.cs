using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using TravelTracker.Web.Data;
using TravelTracker.Web.Data.Entities;
using TravelTracker.Web.Domain;

namespace TravelTracker.Web.Pages.Reports;

// M4a summary report: trips-by-person + cost-by-department + cost-by-category, over
// a date range. Data scope is decided by ReportAccess (Finance/Admin org-wide,
// Manager/Arranger scoped); the RequireReports policy on the folder keeps Employees
// out entirely.
public class IndexModel : ReportPageModel
{
    public IndexModel(AppDbContext db, TeamAccess team) : base(db, team) { }

    [BindProperty(SupportsGet = true)]
    public DateOnly? From { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? To { get; set; }

    [BindProperty(SupportsGet = true)]
    public TripStatus? Status { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? TravelerId { get; set; }

    public ReportSummary Summary { get; private set; } = ReportSummary.Empty;
    public SelectList TravelerFilter { get; private set; } = new(Array.Empty<string>());
    public string? FilterError { get; private set; }

    // Chart data serialized for the Chart.js views (category / department / month).
    // "null" when there's nothing to plot, so the script can bail cleanly.
    public string ChartDataJson { get; private set; } = "null";

    public async Task OnGetAsync()
    {
        var (filter, error) = ReportFilter.Create(From, To, Status, TravelerId);
        FilterError = error;

        await BuildTravelerFilterAsync();

        // On an invalid (inverted) range, show the error and an empty report rather
        // than a misleading partial result.
        Summary = error is null
            ? ReportAggregation.Build(await LoadRowsAsync(filter))
            : ReportSummary.Empty;

        ChartDataJson = BuildChartJson(Summary);
    }

    // Serializes the three chart series. Money is emitted as plain numbers (JS
    // formats for display); month labels are pre-formatted invariantly server-side.
    private static string BuildChartJson(ReportSummary s)
    {
        if (s.TripCount == 0) return "null";

        var data = new
        {
            category = new
            {
                labels = s.ByCategory.Select(c => c.Category.ToString()).ToArray(),
                values = s.ByCategory.Select(c => c.Total).ToArray()
            },
            department = new
            {
                labels = s.ByDepartment.Select(d => d.DepartmentName).ToArray(),
                values = s.ByDepartment.Select(d => d.Combined).ToArray()
            },
            month = new
            {
                labels = s.ByMonth
                    .Select(m => m.Month.ToString("MMM yyyy", CultureInfo.InvariantCulture))
                    .ToArray(),
                values = s.ByMonth.Select(m => m.Combined).ToArray()
            }
        };

        return JsonSerializer.Serialize(data);
    }

    // CSV export of one report view, using the exact same scope + filter as the
    // on-screen tables. Streams a UTF-8 (BOM) file so Excel opens it cleanly. An
    // invalid range redirects back to the page so the user sees the error there.
    public async Task<IActionResult> OnGetExportAsync(string? view)
    {
        var (filter, error) = ReportFilter.Create(From, To, Status, TravelerId);
        if (error is not null)
            return RedirectToPage(new { From, To, Status, TravelerId });

        var summary = ReportAggregation.Build(await LoadRowsAsync(filter));
        var (name, header, rows) = BuildExport(view, summary);

        var stamp = DateOnly.FromDateTime(DateTime.Today).ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var bytes = CsvWriter.ToBytes(header, rows);
        return File(bytes, "text/csv", $"report-{name}-{stamp}.csv");
    }

    private static (string Name, string[] Header, IEnumerable<IEnumerable<string>> Rows)
        BuildExport(string? view, ReportSummary s) => view switch
    {
        "department" => ("by-department",
            new[] { "Department", "Travelers", "Trips", "Expenses", "Mileage", "Combined" },
            s.ByDepartment.Select(d => new[]
            {
                d.DepartmentName, CsvWriter.Number(d.TravelerCount), CsvWriter.Number(d.TripCount),
                CsvWriter.Money(d.ExpenseReimbursable), CsvWriter.Money(d.MileageAmount),
                CsvWriter.Money(d.Combined)
            })),

        "category" => ("by-category",
            new[] { "Category", "Lines", "Total" },
            s.ByCategory.Select(c => new[]
            {
                c.Category.ToString(), CsvWriter.Number(c.Count), CsvWriter.Money(c.Total)
            })),

        "costcenter" => ("by-cost-center",
            new[] { "Cost center", "Trips", "Expenses", "Mileage", "Combined" },
            s.ByCostCenter.Select(cc => new[]
            {
                cc.CostCenter ?? "(unassigned)", CsvWriter.Number(cc.TripCount),
                CsvWriter.Money(cc.ExpenseReimbursable), CsvWriter.Money(cc.MileageAmount),
                CsvWriter.Money(cc.Combined)
            })),

        _ => ("by-person",
            new[] { "Traveler", "Trips", "Travel days", "Expenses", "Mileage", "Combined" },
            s.ByPerson.Select(p => new[]
            {
                p.TravelerName, CsvWriter.Number(p.TripCount), CsvWriter.Number(p.TravelDays),
                CsvWriter.Money(p.ExpenseReimbursable), CsvWriter.Money(p.MileageAmount),
                CsvWriter.Money(p.Combined)
            })),
    };

    // The traveler dropdown lists exactly who the viewer may report on: everyone
    // (org-wide) or their reachable set plus themselves (scoped).
    private async Task BuildTravelerFilterAsync()
    {
        var reachable = await Team.ReachableTravelerIdsAsync(User);
        var scope = ReportAccess.Scope(User, reachable);

        var q = Db.Users.AsNoTracking();
        if (!scope.All)
            q = q.Where(u => scope.TravelerIds.Contains(u.Id));

        var users = await q
            .OrderBy(u => u.DisplayName)
            .Select(u => new { u.Id, u.DisplayName })
            .ToListAsync();

        TravelerFilter = new SelectList(users, "Id", "DisplayName", TravelerId);
    }
}
