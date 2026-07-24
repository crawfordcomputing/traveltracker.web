using TravelTracker.Web.Data.Entities;
using TravelTracker.Web.Domain;
using Xunit;

namespace TravelTracker.Tests;

public class ReportAggregationTests
{
    private static DateOnly D(int m, int d) => new(2026, m, d);

    private static ReportRow Row(
        int id, string travelerId, string travelerName,
        int? deptId, string deptName,
        DateOnly start, DateOnly end,
        decimal reimbursable, decimal personal, decimal mileage,
        string? costCenter = null,
        params (ExpenseCategory, decimal)[] categories) =>
        new(id, travelerId, travelerName, deptId, deptName,
            TripStatus.Completed, start, end, costCenter, TripType.Business,
            reimbursable, personal, mileage,
            categories.Select(c => new CategoryAmount(c.Item1, c.Item2)).ToList());

    [Fact]
    public void Empty_Is_All_Zero()
    {
        var s = ReportAggregation.Build(Array.Empty<ReportRow>());
        Assert.Equal(0, s.TripCount);
        Assert.Equal(0m, s.Combined);
        Assert.Empty(s.ByPerson);
        Assert.Empty(s.ByDepartment);
        Assert.Empty(s.ByCategory);
        Assert.Empty(s.ByCostCenter);
    }

    [Fact]
    public void By_Person_Sums_Trips_Days_And_Money()
    {
        var s = ReportAggregation.Build(new[]
        {
            Row(1, "u1", "Alice", 1, "Eng", D(6, 1), D(6, 3), 100m, 0m, 20m),   // 3 days
            Row(2, "u1", "Alice", 1, "Eng", D(6, 10), D(6, 10), 50m, 0m, 0m),   // 1 day
            Row(3, "u2", "Bob",   1, "Eng", D(6, 5), D(6, 6), 200m, 0m, 0m),    // 2 days
        });

        var alice = Assert.Single(s.ByPerson, p => p.TravelerId == "u1");
        Assert.Equal(2, alice.TripCount);
        Assert.Equal(4, alice.TravelDays);
        Assert.Equal(150m, alice.ExpenseReimbursable);
        Assert.Equal(20m, alice.MileageAmount);
        Assert.Equal(170m, alice.Combined);

        // Highest combined sorts first (Bob 200 > Alice 170).
        Assert.Equal("u2", s.ByPerson[0].TravelerId);
    }

    [Fact]
    public void Personal_Is_Excluded_From_Reimbursable_But_Tracked()
    {
        var s = ReportAggregation.Build(new[]
        {
            Row(1, "u1", "Alice", 1, "Eng", D(6, 1), D(6, 1), 100m, 40m, 0m),
        });
        Assert.Equal(100m, s.ExpenseReimbursable);
        Assert.Equal(40m, s.ExpensePersonal);
        Assert.Equal(100m, s.Combined); // personal never enters combined cost
    }

    [Fact]
    public void By_Department_Counts_Distinct_Travelers()
    {
        var s = ReportAggregation.Build(new[]
        {
            Row(1, "u1", "Alice", 1, "Eng", D(6, 1), D(6, 1), 100m, 0m, 0m),
            Row(2, "u2", "Bob",   1, "Eng", D(6, 1), D(6, 1), 100m, 0m, 0m),
            Row(3, "u1", "Alice", 1, "Eng", D(6, 2), D(6, 2), 100m, 0m, 0m),
        });
        var eng = Assert.Single(s.ByDepartment);
        Assert.Equal(3, eng.TripCount);
        Assert.Equal(2, eng.TravelerCount); // distinct: Alice + Bob
        Assert.Equal(300m, eng.ExpenseReimbursable);
    }

    [Fact]
    public void By_Category_Aggregates_Across_Trips()
    {
        var s = ReportAggregation.Build(new[]
        {
            Row(1, "u1", "Alice", 1, "Eng", D(6, 1), D(6, 1), 150m, 0m, 0m, null,
                (ExpenseCategory.Meals, 50m), (ExpenseCategory.Lodging, 100m)),
            Row(2, "u2", "Bob",   1, "Eng", D(6, 2), D(6, 2), 30m, 0m, 0m, null,
                (ExpenseCategory.Meals, 30m)),
        });

        var meals = Assert.Single(s.ByCategory, c => c.Category == ExpenseCategory.Meals);
        Assert.Equal(80m, meals.Total);
        Assert.Equal(2, meals.Count);
        // Lodging (100) outranks Meals (80).
        Assert.Equal(ExpenseCategory.Lodging, s.ByCategory[0].Category);
    }

    [Fact]
    public void By_Cost_Center_Groups_Unassigned_Together_And_Last()
    {
        var s = ReportAggregation.Build(new[]
        {
            Row(1, "u1", "Alice", 1, "Eng", D(6, 1), D(6, 1), 100m, 0m, 0m, "CC-100"),
            Row(2, "u2", "Bob",   1, "Eng", D(6, 2), D(6, 2), 10m, 0m, 0m, null),
            Row(3, "u3", "Cara",  1, "Eng", D(6, 3), D(6, 3), 10m, 0m, 0m, "  "),
        });

        // Blank + null collapse into one "unassigned" bucket.
        var unassigned = Assert.Single(s.ByCostCenter, c => c.CostCenter is null);
        Assert.Equal(2, unassigned.TripCount);
        Assert.Equal("CC-100", s.ByCostCenter[0].CostCenter); // assigned sorts before unassigned
        Assert.Null(s.ByCostCenter[^1].CostCenter);
    }

    [Fact]
    public void By_Month_Groups_On_Trip_Start_And_Sorts_Chronologically()
    {
        var s = ReportAggregation.Build(new[]
        {
            Row(1, "u1", "Alice", 1, "Eng", D(3, 20), D(3, 22), 100m, 0m, 10m), // Mar
            Row(2, "u2", "Bob",   1, "Eng", D(1, 5), D(1, 6), 50m, 0m, 0m),     // Jan
            Row(3, "u1", "Alice", 1, "Eng", D(3, 1), D(3, 2), 30m, 0m, 0m),     // Mar
        });

        Assert.Equal(2, s.ByMonth.Count);
        // Chronological order: January before March.
        Assert.Equal(new DateOnly(2026, 1, 1), s.ByMonth[0].Month);
        Assert.Equal(new DateOnly(2026, 3, 1), s.ByMonth[1].Month);

        var march = s.ByMonth[1];
        Assert.Equal(2, march.TripCount);
        Assert.Equal(130m, march.ExpenseReimbursable);
        Assert.Equal(10m, march.MileageAmount);
        Assert.Equal(140m, march.Combined);
    }

    [Fact]
    public void Grand_Totals_Match_Row_Sums()
    {
        var s = ReportAggregation.Build(new[]
        {
            Row(1, "u1", "Alice", 1, "Eng", D(6, 1), D(6, 1), 100m, 5m, 20m),
            Row(2, "u2", "Bob",   2, "Sales", D(6, 2), D(6, 2), 200m, 0m, 0m),
        });
        Assert.Equal(2, s.TripCount);
        Assert.Equal(300m, s.ExpenseReimbursable);
        Assert.Equal(5m, s.ExpensePersonal);
        Assert.Equal(20m, s.MileageAmount);
        Assert.Equal(320m, s.Combined);
    }
}
