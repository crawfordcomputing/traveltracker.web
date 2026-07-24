using TravelTracker.Web.Data.Entities;
using TravelTracker.Web.Domain;
using Xunit;

namespace TravelTracker.Tests;

public class ExpenseCloneTests
{
    private static Expense Source() => new()
    {
        Id = 7,
        TripId = 1,
        Category = ExpenseCategory.Lodging,
        Date = new DateOnly(2026, 3, 1),
        Vendor = "Hilton",
        Description = "Two nights",
        Amount = 300m,
        Currency = "USD",
        FxRate = 1m,
        BaseAmount = 300m,
        IsPersonal = false,
        Attendees = "n/a",
        BusinessPurpose = "Onsite",
        ReceiptPath = "receipts/abc.pdf",
        MissingReceiptAffidavit = "was lost",
        Reimbursed = true,
        ReimbursedDate = new DateOnly(2026, 3, 15),
        CreatedById = "old-user",
        Splits =
        {
            new ExpenseSplit { Category = ExpenseCategory.Lodging, Amount = 250m, BaseAmount = 250m },
            new ExpenseSplit { Category = ExpenseCategory.Meals, Amount = 50m, BaseAmount = 50m }
        }
    };

    [Fact]
    public void Copies_Money_And_Substance_To_Target_Trip()
    {
        var now = DateTimeOffset.UtcNow;
        var clone = ExpenseClone.From(Source(), targetTripId: 42, createdById: "me",
            date: new DateOnly(2026, 7, 20), now: now);

        Assert.Equal(42, clone.TripId);
        Assert.Equal(0, clone.Id); // a fresh row
        Assert.Equal(ExpenseCategory.Lodging, clone.Category);
        Assert.Equal(new DateOnly(2026, 7, 20), clone.Date); // rebased
        Assert.Equal("Hilton", clone.Vendor);
        Assert.Equal(300m, clone.Amount);
        Assert.Equal(300m, clone.BaseAmount);
        Assert.Equal("USD", clone.Currency);
        Assert.Equal("me", clone.CreatedById);
        Assert.Equal(now, clone.CreatedAt);
    }

    [Fact]
    public void Does_Not_Carry_Receipt_Or_Lifecycle_State()
    {
        var clone = ExpenseClone.From(Source(), 42, "me", new DateOnly(2026, 7, 20), DateTimeOffset.UtcNow);

        Assert.Null(clone.ReceiptPath);
        Assert.Null(clone.MissingReceiptAffidavit);
        Assert.False(clone.Reimbursed);
        Assert.Null(clone.ReimbursedDate);
    }

    [Fact]
    public void Copies_Splits_As_Fresh_Rows_That_Reconcile()
    {
        var clone = ExpenseClone.From(Source(), 42, "me", new DateOnly(2026, 7, 20), DateTimeOffset.UtcNow);

        Assert.Equal(2, clone.Splits.Count);
        Assert.All(clone.Splits, s => Assert.Equal(0, s.Id));
        Assert.True(ExpenseSplitMath.Reconciles(clone.BaseAmount, clone.Splits.Select(s => s.BaseAmount)));
    }
}
