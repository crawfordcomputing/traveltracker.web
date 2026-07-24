using TravelTracker.Web.Data.Entities;
using TravelTracker.Web.Domain;
using Xunit;

namespace TravelTracker.Tests;

public class MileageRollupTests
{
    [Fact]
    public void Empty_Returns_Zeros()
    {
        var r = MileageRollup.Summarize(Array.Empty<MileageEntry>());
        Assert.Equal(0m, r.Amount);
        Assert.Equal(0, r.Count);
        Assert.Equal(0m, r.MilesBillable);
        Assert.Equal(0m, r.KilometersBillable);
    }

    [Fact]
    public void Sums_Frozen_Amounts_And_Billable_Distance()
    {
        var entries = new[]
        {
            new MileageEntry { Distance = 42m, IsRoundTrip = true, CommuteDeduction = 12m, Unit = DistanceUnit.Miles, Amount = 52.20m },
            new MileageEntry { Distance = 10m, IsRoundTrip = false, CommuteDeduction = 0m, Unit = DistanceUnit.Miles, Amount = 7.00m },
        };
        var r = MileageRollup.Summarize(entries);
        Assert.Equal(59.20m, r.Amount);         // frozen amounts summed
        Assert.Equal(82m, r.MilesBillable);     // 72 + 10 billable
        Assert.Equal(0m, r.KilometersBillable);
        Assert.Equal(2, r.Count);
    }

    [Fact]
    public void Keeps_Miles_And_Kilometers_Separate()
    {
        var entries = new[]
        {
            new MileageEntry { Distance = 10m, Unit = DistanceUnit.Miles, Amount = 7.00m },
            new MileageEntry { Distance = 20m, Unit = DistanceUnit.Kilometers, Amount = 8.00m },
        };
        var r = MileageRollup.Summarize(entries);
        Assert.Equal(10m, r.MilesBillable);
        Assert.Equal(20m, r.KilometersBillable);
        Assert.Equal(15.00m, r.Amount);
    }
}
