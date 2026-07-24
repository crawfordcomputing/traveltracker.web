using TravelTracker.Web.Data.Entities;
using TravelTracker.Web.Domain;
using Xunit;

namespace TravelTracker.Tests;

public class WaypointTextTests
{
    [Fact]
    public void Parse_Numbers_Sequentially_And_Trims()
    {
        var wps = WaypointText.Parse("  SFO Airport \n Client site \nHotel ");
        Assert.Equal(3, wps.Count);
        Assert.Equal((1, "SFO Airport"), (wps[0].Sequence, wps[0].Label));
        Assert.Equal((2, "Client site"), (wps[1].Sequence, wps[1].Label));
        Assert.Equal((3, "Hotel"), (wps[2].Sequence, wps[2].Label));
    }

    [Fact]
    public void Parse_Drops_Blank_Lines()
    {
        var wps = WaypointText.Parse("A\n\n\nB\n   \nC");
        Assert.Equal(new[] { "A", "B", "C" }, wps.Select(w => w.Label));
        Assert.Equal(new[] { 1, 2, 3 }, wps.Select(w => w.Sequence));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n  ")]
    public void Parse_Empty_Returns_No_Waypoints(string? text)
        => Assert.Empty(WaypointText.Parse(text));

    [Fact]
    public void Parse_Handles_Windows_Line_Endings()
        => Assert.Equal(2, WaypointText.Parse("A\r\nB").Count);

    [Fact]
    public void Format_Roundtrips_Ordered_Labels()
    {
        var wps = new List<MileageWaypoint>
        {
            new() { Sequence = 2, Label = "B" },
            new() { Sequence = 1, Label = "A" },
            new() { Sequence = 3, Label = "C" },
        };
        Assert.Equal("A\nB\nC", WaypointText.Format(wps));
    }
}
