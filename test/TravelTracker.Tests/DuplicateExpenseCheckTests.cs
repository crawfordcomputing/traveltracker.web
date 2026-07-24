using TravelTracker.Web.Data.Entities;
using TravelTracker.Web.Domain;
using Xunit;

namespace TravelTracker.Tests;

public class DuplicateExpenseCheckTests
{
    private static Expense Line(int id, decimal amount, string date, string? vendor) => new()
    {
        Id = id,
        Amount = amount,
        Date = DateOnly.Parse(date),
        Vendor = vendor
    };

    [Fact]
    public void Flags_When_Amount_Date_Vendor_All_Match()
    {
        var candidate = Line(0, 42.50m, "2026-07-10", "Hilton");
        var existing = new[] { Line(1, 42.50m, "2026-07-10", "Hilton") };
        Assert.True(DuplicateExpenseCheck.HasMatch(candidate, existing));
    }

    [Fact]
    public void Vendor_Match_Is_Case_And_Whitespace_Insensitive()
    {
        var candidate = Line(0, 42.50m, "2026-07-10", "  hILTon ");
        var existing = new[] { Line(1, 42.50m, "2026-07-10", "Hilton") };
        Assert.True(DuplicateExpenseCheck.HasMatch(candidate, existing));
    }

    [Theory]
    [InlineData(43.00, "2026-07-10", "Hilton")] // amount differs
    [InlineData(42.50, "2026-07-11", "Hilton")] // date differs
    [InlineData(42.50, "2026-07-10", "Marriott")] // vendor differs
    public void Does_Not_Flag_When_Any_Field_Differs(decimal amount, string date, string vendor)
    {
        var candidate = Line(0, 42.50m, "2026-07-10", "Hilton");
        var existing = new[] { Line(1, amount, date, vendor) };
        Assert.False(DuplicateExpenseCheck.HasMatch(candidate, existing));
    }

    [Fact]
    public void Null_Or_Blank_Vendor_Never_Matches()
    {
        var candidate = Line(0, 42.50m, "2026-07-10", "   ");
        var existing = new[] { Line(1, 42.50m, "2026-07-10", null) };
        Assert.Empty(DuplicateExpenseCheck.FindMatches(candidate, existing));
    }

    [Fact]
    public void Excludes_Self_By_Id_When_Editing()
    {
        var candidate = Line(5, 42.50m, "2026-07-10", "Hilton");
        var existing = new[] { Line(5, 42.50m, "2026-07-10", "Hilton") };
        Assert.Empty(DuplicateExpenseCheck.FindMatches(candidate, existing));
    }

    [Fact]
    public void Returns_All_Matching_Lines()
    {
        var candidate = Line(0, 20m, "2026-07-10", "Uber");
        var existing = new[]
        {
            Line(1, 20m, "2026-07-10", "Uber"),
            Line(2, 20m, "2026-07-10", "uber"),
            Line(3, 20m, "2026-07-11", "Uber") // different date, excluded
        };
        Assert.Equal(2, DuplicateExpenseCheck.FindMatches(candidate, existing).Count);
    }
}
