using TravelTracker.Web.Domain;
using Xunit;

namespace TravelTracker.Tests;

public class MileageMathTests
{
    [Theory]
    [InlineData(10, false, 0, 10)]    // one-way, no deduction
    [InlineData(10, true, 0, 20)]     // round trip doubles
    [InlineData(10, false, 3, 7)]     // commute deducted
    [InlineData(10, true, 5, 15)]     // round trip then deduct
    [InlineData(10, false, 15, 0)]    // over-large deduction floors at 0
    [InlineData(10, true, 25, 0)]     // deduction exceeds round-trip distance
    public void BillableDistance_Matches_Table(decimal distance, bool roundTrip, decimal commute, decimal expected)
        => Assert.Equal(expected, MileageMath.BillableDistance(distance, roundTrip, commute));

    [Fact]
    public void Amount_Multiplies_And_Rounds_To_Two_Decimals()
    {
        // 27.4 mi * 0.725 = 19.865 → 19.87 (away-from-zero)
        Assert.Equal(19.87m, MileageMath.Amount(27.4m, 0.725m));
    }

    [Fact]
    public void Amount_Is_Zero_When_No_Billable_Distance()
        => Assert.Equal(0m, MileageMath.Amount(0m, 0.700m));

    [Fact]
    public void BillableDistance_Then_Amount_Compose()
    {
        var billable = MileageMath.BillableDistance(42m, isRoundTrip: true, commuteDeduction: 12m); // 72
        Assert.Equal(72m, billable);
        Assert.Equal(52.20m, MileageMath.Amount(billable, 0.725m)); // 72 * 0.725 = 52.20
    }
}
