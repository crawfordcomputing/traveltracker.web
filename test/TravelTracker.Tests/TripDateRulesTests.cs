using TravelTracker.Web.Data.Entities;
using TravelTracker.Web.Domain;
using Xunit;

namespace TravelTracker.Tests;

public class TripDateRulesTests
{
    private static DateOnly D(int day) => new(2026, 7, day);

    [Theory]
    [InlineData(10, 10, true)]   // single-day trip
    [InlineData(10, 12, true)]   // normal range
    [InlineData(12, 10, false)]  // end before start
    public void IsValidTripRange_Matches_Table(int start, int end, bool expected)
        => Assert.Equal(expected, TripDateRules.IsValidTripRange(D(start), D(end)));

    [Fact]
    public void ValidateLeg_Allows_Valid_Interval()
        => Assert.Null(TripDateRules.ValidateLeg(D(12), D(15)));

    [Fact]
    public void ValidateLeg_Allows_Single_Day()
        => Assert.Null(TripDateRules.ValidateLeg(D(12), D(12)));

    [Fact]
    public void ValidateLeg_Rejects_Depart_Before_Arrive()
        => Assert.NotNull(TripDateRules.ValidateLeg(D(15), D(12)));

    [Fact]
    public void ReconcileWindow_Keeps_Window_When_Legs_Inside()
    {
        var legs = new[]
        {
            new Destination { ArriveDate = D(12), DepartDate = D(14) },
            new Destination { ArriveDate = D(15), DepartDate = D(18) },
        };
        var (start, end) = TripDateRules.ReconcileWindow(D(10), D(20), legs);
        Assert.Equal(D(10), start);   // buffer preserved
        Assert.Equal(D(20), end);
    }

    [Fact]
    public void ReconcileWindow_Expands_To_Fit_Outlying_Legs()
    {
        var legs = new[]
        {
            new Destination { ArriveDate = D(8), DepartDate = D(14) },   // before start
            new Destination { ArriveDate = D(19), DepartDate = D(25) },  // after end
        };
        var (start, end) = TripDateRules.ReconcileWindow(D(10), D(20), legs);
        Assert.Equal(D(8), start);
        Assert.Equal(D(25), end);
    }

    [Fact]
    public void ReconcileWindow_NoLegs_Returns_Original()
    {
        var (start, end) = TripDateRules.ReconcileWindow(D(10), D(20), Array.Empty<Destination>());
        Assert.Equal(D(10), start);
        Assert.Equal(D(20), end);
    }
}
