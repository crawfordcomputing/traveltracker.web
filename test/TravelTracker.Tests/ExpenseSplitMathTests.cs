using TravelTracker.Web.Domain;
using Xunit;

namespace TravelTracker.Tests;

public class ExpenseSplitMathTests
{
    [Fact]
    public void Reconciles_When_Splits_Sum_To_Parent()
        => Assert.True(ExpenseSplitMath.Reconciles(100m, new[] { 60m, 40m }));

    [Fact]
    public void Does_Not_Reconcile_When_Off_By_A_Cent()
        => Assert.False(ExpenseSplitMath.Reconciles(100m, new[] { 60m, 39.99m }));

    [Fact]
    public void Does_Not_Reconcile_When_Over_Allocated()
        => Assert.False(ExpenseSplitMath.Reconciles(100m, new[] { 60m, 50m }));

    [Fact]
    public void Empty_Set_Only_Reconciles_A_Zero_Parent()
    {
        Assert.False(ExpenseSplitMath.Reconciles(100m, Array.Empty<decimal>()));
        Assert.True(ExpenseSplitMath.Reconciles(0m, Array.Empty<decimal>()));
    }

    [Fact]
    public void Remainder_Reports_Unallocated_Signed()
    {
        Assert.Equal(25m, ExpenseSplitMath.Remainder(100m, new[] { 75m }));   // under
        Assert.Equal(-10m, ExpenseSplitMath.Remainder(100m, new[] { 110m }));  // over
        Assert.Equal(0m, ExpenseSplitMath.Remainder(100m, new[] { 100m }));    // exact
    }

    [Fact]
    public void DistributeEvenly_Last_Part_Absorbs_Rounding()
    {
        var parts = ExpenseSplitMath.DistributeEvenly(100m, 3);
        Assert.Equal(new[] { 33.33m, 33.33m, 33.34m }, parts);
        Assert.Equal(100m, parts.Sum());
        Assert.True(ExpenseSplitMath.Reconciles(100m, parts));
    }

    [Fact]
    public void DistributeEvenly_Divides_Cleanly_When_It_Can()
    {
        var parts = ExpenseSplitMath.DistributeEvenly(90m, 3);
        Assert.Equal(new[] { 30m, 30m, 30m }, parts);
    }

    [Fact]
    public void DistributeEvenly_Single_Part_Is_The_Whole()
        => Assert.Equal(new[] { 42.50m }, ExpenseSplitMath.DistributeEvenly(42.50m, 1));

    [Fact]
    public void DistributeEvenly_Rejects_Non_Positive_Count()
        => Assert.Throws<ArgumentOutOfRangeException>(() => ExpenseSplitMath.DistributeEvenly(100m, 0));
}
