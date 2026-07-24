using TravelTracker.Web.Data.Entities;
using TravelTracker.Web.Domain;
using Xunit;

namespace TravelTracker.Tests;

public class TripStatusRulesTests
{
    [Theory]
    [InlineData(TripStatus.Draft, TripStatus.Planned, true)]
    [InlineData(TripStatus.Draft, TripStatus.Cancelled, true)]
    [InlineData(TripStatus.Draft, TripStatus.Completed, false)]
    [InlineData(TripStatus.Planned, TripStatus.Completed, true)]
    [InlineData(TripStatus.Planned, TripStatus.Cancelled, true)]
    [InlineData(TripStatus.Planned, TripStatus.Draft, true)]
    [InlineData(TripStatus.Completed, TripStatus.Draft, false)]
    [InlineData(TripStatus.Completed, TripStatus.Planned, true)]
    [InlineData(TripStatus.Cancelled, TripStatus.Draft, true)]
    [InlineData(TripStatus.Cancelled, TripStatus.Completed, false)]
    public void CanTransition_Matches_Table(TripStatus from, TripStatus to, bool expected)
        => Assert.Equal(expected, TripStatusRules.CanTransition(from, to));

    [Fact]
    public void NextStates_From_Draft_Are_Plan_Or_Cancel()
    {
        var next = TripStatusRules.NextStates(TripStatus.Draft);
        Assert.Contains(TripStatus.Planned, next);
        Assert.Contains(TripStatus.Cancelled, next);
        Assert.DoesNotContain(TripStatus.Completed, next);
    }
}
