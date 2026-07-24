using TravelTracker.Web.Data.Entities;
using TravelTracker.Web.Domain;
using Xunit;

namespace TravelTracker.Tests;

public class TripOverlapTests
{
    private static DateOnly D(int day) => new(2026, 7, day);

    [Theory]
    [InlineData(10, 15, 16, 20, false)]  // adjacent, no overlap
    [InlineData(10, 15, 15, 20, true)]   // touch on boundary day
    [InlineData(10, 20, 12, 14, true)]   // fully contained
    [InlineData(10, 15, 5, 8, false)]    // entirely before
    public void Overlaps_Matches_Table(int aS, int aE, int bS, int bE, bool expected)
        => Assert.Equal(expected, TripOverlap.Overlaps(D(aS), D(aE), D(bS), D(bE)));

    [Fact]
    public void FindConflicts_Ignores_Self_And_Cancelled()
    {
        var trips = new[]
        {
            new Trip { Id = 1, Status = TripStatus.Planned, StartDate = D(10), EndDate = D(15) },
            new Trip { Id = 2, Status = TripStatus.Cancelled, StartDate = D(10), EndDate = D(15) },
            new Trip { Id = 3, Status = TripStatus.Planned, StartDate = D(12), EndDate = D(18) },
        };

        var conflicts = TripOverlap.FindConflicts(trips, D(11), D(14), excludeTripId: 1).ToList();

        Assert.Single(conflicts);
        Assert.Equal(3, conflicts[0].Id);   // #1 excluded (self), #2 excluded (cancelled)
    }

    [Fact]
    public void FindConflicts_Empty_When_No_Overlap()
    {
        var trips = new[]
        {
            new Trip { Id = 5, Status = TripStatus.Planned, StartDate = D(1), EndDate = D(5) },
        };
        Assert.Empty(TripOverlap.FindConflicts(trips, D(10), D(15)));
    }
}
