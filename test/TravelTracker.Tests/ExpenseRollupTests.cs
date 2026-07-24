using TravelTracker.Web.Data.Entities;
using TravelTracker.Web.Domain;
using Xunit;

namespace TravelTracker.Tests;

public class ExpenseRollupTests
{
    private static Expense Line(ExpenseCategory cat, decimal baseAmount, bool personal = false,
        params ExpenseSplit[] splits) => new()
    {
        Category = cat,
        Amount = baseAmount,
        BaseAmount = baseAmount,
        IsPersonal = personal,
        Splits = splits.ToList()
    };

    private static ExpenseSplit Split(ExpenseCategory cat, decimal baseAmount) =>
        new() { Category = cat, Amount = baseAmount, BaseAmount = baseAmount };

    [Fact]
    public void Empty_Is_All_Zero()
    {
        var r = ExpenseRollup.Summarize(Array.Empty<Expense>());
        Assert.Equal(0m, r.Total);
        Assert.Equal(0, r.Count);
        Assert.Empty(r.ByCategory);
    }

    [Fact]
    public void Splits_Reimbursable_From_Personal()
    {
        var r = ExpenseRollup.Summarize(new[]
        {
            Line(ExpenseCategory.Lodging, 200m),
            Line(ExpenseCategory.Meals, 50m, personal: true)
        });
        Assert.Equal(200m, r.Reimbursable);
        Assert.Equal(50m, r.Personal);
        Assert.Equal(250m, r.Total);
    }

    [Fact]
    public void Split_Line_Attributes_To_Split_Categories_Not_Parent()
    {
        // A $300 hotel folio split into Lodging 250 + Meals 50.
        var folio = Line(ExpenseCategory.Lodging, 300m, false,
            Split(ExpenseCategory.Lodging, 250m),
            Split(ExpenseCategory.Meals, 50m));

        var r = ExpenseRollup.Summarize(new[] { folio });

        var byCat = r.ByCategory.ToDictionary(l => l.Category, l => l.Total);
        Assert.Equal(250m, byCat[ExpenseCategory.Lodging]);
        Assert.Equal(50m, byCat[ExpenseCategory.Meals]);
        // Totals unchanged: splits reconcile to the parent.
        Assert.Equal(300m, r.Total);
        Assert.Equal(300m, r.Reimbursable);
    }

    [Fact]
    public void Non_Reconciling_Splits_Fall_Back_To_Parent_Category()
    {
        // A half-finished split set (only 250 of 300 allocated) must not drift totals
        // or leak a phantom category — the parent's own category is used instead.
        var partial = Line(ExpenseCategory.Lodging, 300m, false,
            Split(ExpenseCategory.Lodging, 250m));

        var r = ExpenseRollup.Summarize(new[] { partial });

        Assert.Single(r.ByCategory);
        Assert.Equal(ExpenseCategory.Lodging, r.ByCategory[0].Category);
        Assert.Equal(300m, r.ByCategory[0].Total);
        Assert.Equal(300m, r.Total);
    }

    [Fact]
    public void Rollup_With_And_Without_Splits_Have_Equal_Totals()
    {
        var withSplits = ExpenseRollup.Summarize(new[]
        {
            Line(ExpenseCategory.Lodging, 300m, false,
                Split(ExpenseCategory.Lodging, 250m),
                Split(ExpenseCategory.Meals, 50m))
        });
        var without = ExpenseRollup.Summarize(new[] { Line(ExpenseCategory.Lodging, 300m) });

        Assert.Equal(without.Total, withSplits.Total);
        Assert.Equal(without.Reimbursable, withSplits.Reimbursable);
    }
}
