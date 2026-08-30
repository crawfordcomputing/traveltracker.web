using TravelTracker.Web.Data.Entities;
using TravelTracker.Web.Domain;
using Xunit;

namespace TravelTracker.Tests;

public class ExpensePolicyCheckTests
{
    private static readonly List<ExpensePolicy> Policies = new()
    {
        new ExpensePolicy { Category = ExpenseCategory.Lodging, CapAmount = 350m },
        new ExpensePolicy { Category = ExpenseCategory.Meals, CapAmount = 75m },
    };

    private static IReadOnlyDictionary<ExpenseCategory, decimal> Caps() =>
        ExpensePolicyCheck.CapsFrom(Policies);

    private static Expense Line(ExpenseCategory cat, decimal baseAmount) =>
        new() { Category = cat, BaseAmount = baseAmount };

    [Fact]
    public void CapsFrom_Maps_Category_To_Amount()
    {
        var caps = Caps();
        Assert.Equal(350m, caps[ExpenseCategory.Lodging]);
        Assert.False(caps.ContainsKey(ExpenseCategory.Airfare));
    }

    [Fact]
    public void Over_Cap_Is_Flagged()
    {
        var caps = Caps();
        Assert.True(ExpensePolicyCheck.IsOverCap(Line(ExpenseCategory.Lodging, 540m), caps));
        Assert.NotNull(ExpensePolicyCheck.Flag(Line(ExpenseCategory.Lodging, 540m), caps));
    }

    [Theory]
    [InlineData(350)] // exactly at cap is within policy
    [InlineData(200)]
    public void At_Or_Under_Cap_Is_Not_Flagged(decimal amount)
    {
        var caps = Caps();
        Assert.False(ExpensePolicyCheck.IsOverCap(Line(ExpenseCategory.Lodging, amount), caps));
        Assert.Null(ExpensePolicyCheck.Flag(Line(ExpenseCategory.Lodging, amount), caps));
    }

    [Fact]
    public void Uncapped_Category_Is_Never_Flagged()
    {
        var caps = Caps();
        Assert.Null(ExpensePolicyCheck.CapFor(ExpenseCategory.Airfare, caps));
        Assert.False(ExpensePolicyCheck.IsOverCap(Line(ExpenseCategory.Airfare, 5000m), caps));
        Assert.Null(ExpensePolicyCheck.Flag(Line(ExpenseCategory.Airfare, 5000m), caps));
    }

    [Fact]
    public void No_Caps_Configured_Flags_Nothing()
    {
        Assert.Null(ExpensePolicyCheck.Flag(Line(ExpenseCategory.Lodging, 540m), ExpensePolicyCheck.NoCaps));
    }
}
