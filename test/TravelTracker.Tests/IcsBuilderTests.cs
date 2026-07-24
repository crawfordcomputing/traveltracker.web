using TravelTracker.Web.Data.Entities;
using TravelTracker.Web.Domain;
using Xunit;

namespace TravelTracker.Tests;

public class IcsBuilderTests
{
    private static readonly DateTimeOffset Stamp =
        new(2026, 7, 24, 12, 0, 0, TimeSpan.Zero);

    private static Trip TripWith(params Destination[] legs) => new()
    {
        Id = 42,
        Purpose = "Client visit",
        StartDate = new DateOnly(2026, 7, 20),
        EndDate = new DateOnly(2026, 7, 23),
        Destinations = legs.ToList()
    };

    private static Destination Leg(int id, int seq, string city, DateOnly arrive, DateOnly depart) => new()
    {
        Id = id, Sequence = seq, City = city, State = "CA", Country = "United States",
        ArriveDate = arrive, DepartDate = depart
    };

    [Fact]
    public void Wraps_In_Vcalendar_With_Required_Headers()
    {
        var trip = TripWith(Leg(1, 1, "Austin",
            new DateOnly(2026, 7, 20), new DateOnly(2026, 7, 22)));
        var ics = IcsBuilder.Build(trip, trip.Destinations, Stamp);
        Assert.StartsWith("BEGIN:VCALENDAR\r\n", ics);
        Assert.Contains("VERSION:2.0\r\n", ics);
        Assert.EndsWith("END:VCALENDAR\r\n", ics);
    }

    [Fact]
    public void One_Vevent_Per_Leg_With_Stable_Uid()
    {
        var trip = TripWith(
            Leg(1, 1, "Austin", new DateOnly(2026, 7, 20), new DateOnly(2026, 7, 21)),
            Leg(2, 2, "Dallas", new DateOnly(2026, 7, 22), new DateOnly(2026, 7, 23)));

        var ics = IcsBuilder.Build(trip, trip.Destinations, Stamp);

        Assert.Equal(2, Occurrences(ics, "BEGIN:VEVENT"));
        Assert.Contains("UID:trip-42-leg-1@traveltracker.local\r\n", ics);
        Assert.Contains("UID:trip-42-leg-2@traveltracker.local\r\n", ics);
    }

    [Fact]
    public void All_Day_Dtend_Is_Exclusive()
    {
        var trip = TripWith(Leg(1, 1, "Austin",
            new DateOnly(2026, 7, 20), new DateOnly(2026, 7, 22)));
        var ics = IcsBuilder.Build(trip, trip.Destinations, Stamp);

        Assert.Contains("DTSTART;VALUE=DATE:20260720\r\n", ics);
        // Depart 22nd -> exclusive end 23rd so the event covers the 22nd.
        Assert.Contains("DTEND;VALUE=DATE:20260723\r\n", ics);
    }

    [Fact]
    public void Location_Joins_City_State_Country()
    {
        var trip = TripWith(Leg(1, 1, "Austin",
            new DateOnly(2026, 7, 20), new DateOnly(2026, 7, 21)));
        var ics = IcsBuilder.Build(trip, trip.Destinations, Stamp);
        Assert.Contains("LOCATION:Austin\\, CA\\, United States\r\n", ics);
    }

    [Fact]
    public void Escapes_Special_Characters_In_Text()
    {
        var trip = TripWith(Leg(1, 1, "Paris", new DateOnly(2026, 7, 20), new DateOnly(2026, 7, 21)));
        trip.Purpose = "Meet; greet, and go";
        var ics = IcsBuilder.Build(trip, trip.Destinations, Stamp);
        Assert.Contains("SUMMARY:Meet\\; greet\\, and go: Paris\r\n", ics);
    }

    [Fact]
    public void Trip_Without_Legs_Falls_Back_To_A_Trip_Window_Event()
    {
        var trip = TripWith(); // no legs
        var ics = IcsBuilder.Build(trip, trip.Destinations, Stamp);

        Assert.Equal(1, Occurrences(ics, "BEGIN:VEVENT"));
        Assert.Contains("UID:trip-42@traveltracker.local\r\n", ics);
        Assert.Contains("DTSTART;VALUE=DATE:20260720\r\n", ics);
        Assert.Contains("DTEND;VALUE=DATE:20260724\r\n", ics); // end 23rd + 1
    }

    [Fact]
    public void Dtstamp_Is_Utc_Basic_Format()
    {
        var trip = TripWith(Leg(1, 1, "Austin", new DateOnly(2026, 7, 20), new DateOnly(2026, 7, 21)));
        var ics = IcsBuilder.Build(trip, trip.Destinations, Stamp);
        Assert.Contains("DTSTAMP:20260724T120000Z\r\n", ics);
    }

    private static int Occurrences(string haystack, string needle)
    {
        int count = 0, i = 0;
        while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { count++; i += needle.Length; }
        return count;
    }
}
