using TravelTracker.Web.Data.Entities;

namespace TravelTracker.Web.Domain;

// Pure aggregation for the M4 reports. Takes a flat list of ReportRow (one per
// trip, already scoped + date-filtered + projected by the page from EF) and rolls
// it up four ways: by person, by department, by expense category, and by cost
// center. No DB, no I/O — unit-testable in isolation, and the SAME helper feeds
// both the on-screen tables (M4a) and the dashboard charts (M4c) so they never
// disagree.
//
// Money is already frozen upstream: expense figures are BaseAmount, mileage is the
// entry's frozen Amount. Reimbursable EXCLUDES personal / non-reimbursable lines;
// the personal figure is kept separately (it still reconciles against the card feed
// in M7). "Combined" = reimbursable expense + mileage — the org's real cost.
public static class ReportAggregation
{
    public static ReportSummary Build(IEnumerable<ReportRow> rows)
    {
        var list = rows as IReadOnlyList<ReportRow> ?? rows.ToList();

        var byPerson = list
            .GroupBy(r => (r.TravelerId, r.TravelerName))
            .Select(g => new PersonReportLine(
                g.Key.TravelerId,
                g.Key.TravelerName,
                TripCount: g.Count(),
                TravelDays: g.Sum(r => r.TravelDays),
                ExpenseReimbursable: g.Sum(r => r.ExpenseReimbursable),
                MileageAmount: g.Sum(r => r.MileageAmount)))
            .OrderByDescending(l => l.Combined)
            .ThenBy(l => l.TravelerName)
            .ToList();

        var byDepartment = list
            .GroupBy(r => (r.DepartmentId, r.DepartmentName))
            .Select(g => new DepartmentReportLine(
                g.Key.DepartmentId,
                g.Key.DepartmentName,
                TripCount: g.Count(),
                TravelerCount: g.Select(r => r.TravelerId).Distinct().Count(),
                ExpenseReimbursable: g.Sum(r => r.ExpenseReimbursable),
                MileageAmount: g.Sum(r => r.MileageAmount)))
            .OrderByDescending(l => l.Combined)
            .ThenBy(l => l.DepartmentName)
            .ToList();

        var byCategory = list
            .SelectMany(r => r.Categories)
            .GroupBy(c => c.Category)
            .Select(g => new CategoryReportLine(g.Key, g.Sum(c => c.Amount), g.Count()))
            .OrderByDescending(l => l.Total)
            .ThenBy(l => l.Category)
            .ToList();

        // Monthly trend: a trip's spend is attributed to its start month. Ordered
        // chronologically (not by size) so the chart reads left-to-right as a timeline.
        var byMonth = list
            .GroupBy(r => new DateOnly(r.Start.Year, r.Start.Month, 1))
            .Select(g => new MonthReportLine(
                g.Key,
                TripCount: g.Count(),
                ExpenseReimbursable: g.Sum(r => r.ExpenseReimbursable),
                MileageAmount: g.Sum(r => r.MileageAmount)))
            .OrderBy(l => l.Month)
            .ToList();

        var byCostCenter = list
            .GroupBy(r => string.IsNullOrWhiteSpace(r.CostCenter) ? null : r.CostCenter.Trim())
            .Select(g => new CostCenterReportLine(
                g.Key,
                TripCount: g.Count(),
                ExpenseReimbursable: g.Sum(r => r.ExpenseReimbursable),
                MileageAmount: g.Sum(r => r.MileageAmount)))
            .OrderByDescending(l => l.Combined)
            .ThenBy(l => l.CostCenter ?? "￿")   // unassigned sorts last
            .ToList();

        return new ReportSummary(
            byPerson, byDepartment, byCategory, byCostCenter, byMonth,
            TripCount: list.Count,
            ExpenseReimbursable: list.Sum(r => r.ExpenseReimbursable),
            ExpensePersonal: list.Sum(r => r.ExpensePersonal),
            MileageAmount: list.Sum(r => r.MileageAmount));
    }
}

// One trip's contribution, projected by the page from EF (trip + its traveler /
// department + pre-summed expense & mileage money). Categories carries the expense
// category breakdown for this trip (BaseAmount already attributed to split
// categories where present — see ExpenseRollup).
public sealed record ReportRow(
    int TripId,
    string TravelerId,
    string TravelerName,
    int? DepartmentId,
    string DepartmentName,
    TripStatus Status,
    DateOnly Start,
    DateOnly End,
    string? CostCenter,
    TripType Type,
    decimal ExpenseReimbursable,
    decimal ExpensePersonal,
    decimal MileageAmount,
    IReadOnlyList<CategoryAmount> Categories)
{
    // Inclusive day span of the trip window (a same-day trip is 1 day).
    public int TravelDays => End.DayNumber - Start.DayNumber + 1;
}

public sealed record CategoryAmount(ExpenseCategory Category, decimal Amount);

public sealed record PersonReportLine(
    string TravelerId, string TravelerName,
    int TripCount, int TravelDays,
    decimal ExpenseReimbursable, decimal MileageAmount)
{
    public decimal Combined => ExpenseReimbursable + MileageAmount;
}

public sealed record DepartmentReportLine(
    int? DepartmentId, string DepartmentName,
    int TripCount, int TravelerCount,
    decimal ExpenseReimbursable, decimal MileageAmount)
{
    public decimal Combined => ExpenseReimbursable + MileageAmount;
}

public sealed record CategoryReportLine(ExpenseCategory Category, decimal Total, int Count);

// One month's spend (keyed to the first of the month). Month is the trip start month.
public sealed record MonthReportLine(
    DateOnly Month, int TripCount,
    decimal ExpenseReimbursable, decimal MileageAmount)
{
    public decimal Combined => ExpenseReimbursable + MileageAmount;
}

public sealed record CostCenterReportLine(
    string? CostCenter, int TripCount,
    decimal ExpenseReimbursable, decimal MileageAmount)
{
    public decimal Combined => ExpenseReimbursable + MileageAmount;
}

public sealed record ReportSummary(
    IReadOnlyList<PersonReportLine> ByPerson,
    IReadOnlyList<DepartmentReportLine> ByDepartment,
    IReadOnlyList<CategoryReportLine> ByCategory,
    IReadOnlyList<CostCenterReportLine> ByCostCenter,
    IReadOnlyList<MonthReportLine> ByMonth,
    int TripCount,
    decimal ExpenseReimbursable,
    decimal ExpensePersonal,
    decimal MileageAmount)
{
    public decimal Combined => ExpenseReimbursable + MileageAmount;

    public static readonly ReportSummary Empty = new(
        Array.Empty<PersonReportLine>(), Array.Empty<DepartmentReportLine>(),
        Array.Empty<CategoryReportLine>(), Array.Empty<CostCenterReportLine>(),
        Array.Empty<MonthReportLine>(),
        0, 0m, 0m, 0m);
}
