using TravelTracker.Web.Data.Entities;
using TravelTracker.Web.Domain;
using Xunit;

namespace TravelTracker.Tests;

public class ReportFilterTests
{
    private static DateOnly D(int y, int m, int d) => new(y, m, d);

    [Fact]
    public void Empty_Range_Is_Valid_And_Includes_Everything()
    {
        var (filter, error) = ReportFilter.Create(null, null, null, null);
        Assert.Null(error);
        Assert.True(filter.IncludesWindow(D(2020, 1, 1), D(2020, 1, 2)));
        Assert.True(filter.IncludesWindow(D(2099, 12, 30), D(2099, 12, 31)));
    }

    [Fact]
    public void Inverted_Range_Reports_Error()
    {
        var (_, error) = ReportFilter.Create(D(2026, 6, 1), D(2026, 5, 1), null, null);
        Assert.NotNull(error);
    }

    [Fact]
    public void Equal_From_And_To_Is_Valid()
    {
        var (_, error) = ReportFilter.Create(D(2026, 6, 1), D(2026, 6, 1), null, null);
        Assert.Null(error);
    }

    [Fact]
    public void Blank_TravelerId_Normalizes_To_Null()
    {
        var (filter, _) = ReportFilter.Create(null, null, null, "   ");
        Assert.Null(filter.TravelerId);
    }

    [Theory]
    // trip window (start,end) vs range [Jun 10, Jun 20]
    [InlineData(6, 1, 6, 5, false)]    // entirely before
    [InlineData(6, 25, 6, 30, false)]  // entirely after
    [InlineData(6, 5, 6, 12, true)]    // overlaps start edge
    [InlineData(6, 18, 6, 25, true)]   // overlaps end edge
    [InlineData(6, 12, 6, 15, true)]   // fully inside
    [InlineData(6, 1, 6, 30, true)]    // spans the whole range
    public void IncludesWindow_Is_An_Overlap_Test(int sm, int sd, int em, int ed, bool expected)
    {
        var (filter, _) = ReportFilter.Create(D(2026, 6, 10), D(2026, 6, 20), null, null);
        Assert.Equal(expected, filter.IncludesWindow(D(2026, sm, sd), D(2026, em, ed)));
    }

    [Fact]
    public void Open_From_Only_Excludes_Trips_After_To()
    {
        var (filter, _) = ReportFilter.Create(null, D(2026, 6, 20), null, null);
        Assert.True(filter.IncludesWindow(D(2000, 1, 1), D(2000, 1, 2)));
        Assert.False(filter.IncludesWindow(D(2026, 6, 21), D(2026, 6, 22)));
    }
}
