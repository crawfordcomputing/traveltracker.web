using System.Globalization;
using System.Text;
using TravelTracker.Web.Data.Entities;

namespace TravelTracker.Web.Domain;

// Builds an RFC 5545 iCalendar (.ics) for a trip: one all-day VEVENT per itinerary
// leg (legs are date-only today, so VALUE=DATE), with a stable per-leg UID so a
// re-import updates rather than duplicates. A trip with no legs falls back to a
// single event spanning the trip window. Pure and unit-testable — no DB, no HTTP.
//
// All-day DTEND is EXCLUSIVE in iCalendar, so the depart date is emitted as
// depart + 1 day for the event to visually cover the depart day.
public static class IcsBuilder
{
    private const string ProdId = "-//TravelTracker//Travel Tracker//EN";
    private const string UidDomain = "traveltracker.local";

    public static string Build(Trip trip, IEnumerable<Destination> legs, DateTimeOffset stamp)
    {
        var stampUtc = stamp.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

        var sb = new StringBuilder();
        AppendLine(sb, "BEGIN:VCALENDAR");
        AppendLine(sb, "VERSION:2.0");
        AppendLine(sb, "PRODID:" + ProdId);
        AppendLine(sb, "CALSCALE:GREGORIAN");
        AppendLine(sb, "METHOD:PUBLISH");

        var ordered = legs.OrderBy(l => l.Sequence).ThenBy(l => l.Id).ToList();
        if (ordered.Count == 0)
        {
            AppendEvent(sb, stampUtc,
                uid: $"trip-{trip.Id}@{UidDomain}",
                start: trip.StartDate, endExclusive: trip.EndDate.AddDays(1),
                summary: trip.Purpose,
                location: null,
                description: DescribeTrip(trip));
        }
        else
        {
            foreach (var leg in ordered)
            {
                AppendEvent(sb, stampUtc,
                    uid: $"trip-{trip.Id}-leg-{leg.Id}@{UidDomain}",
                    start: leg.ArriveDate, endExclusive: leg.DepartDate.AddDays(1),
                    summary: $"{trip.Purpose}: {leg.City}",
                    location: LocationOf(leg),
                    description: DescribeLeg(leg));
            }
        }

        AppendLine(sb, "END:VCALENDAR");
        return sb.ToString();
    }

    public static byte[] ToBytes(Trip trip, IEnumerable<Destination> legs, DateTimeOffset stamp)
        => Encoding.UTF8.GetBytes(Build(trip, legs, stamp));

    private static void AppendEvent(
        StringBuilder sb, string stampUtc, string uid,
        DateOnly start, DateOnly endExclusive,
        string summary, string? location, string? description)
    {
        AppendLine(sb, "BEGIN:VEVENT");
        AppendLine(sb, "UID:" + uid);
        AppendLine(sb, "DTSTAMP:" + stampUtc);
        AppendLine(sb, "DTSTART;VALUE=DATE:" + Date(start));
        AppendLine(sb, "DTEND;VALUE=DATE:" + Date(endExclusive));
        AppendLine(sb, "SUMMARY:" + Escape(summary));
        if (!string.IsNullOrWhiteSpace(location))
            AppendLine(sb, "LOCATION:" + Escape(location));
        if (!string.IsNullOrWhiteSpace(description))
            AppendLine(sb, "DESCRIPTION:" + Escape(description));
        AppendLine(sb, "END:VEVENT");
    }

    private static string Date(DateOnly d) => d.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

    private static string? LocationOf(Destination leg)
    {
        var parts = new[] { leg.City, leg.State, leg.Country }
            .Where(s => !string.IsNullOrWhiteSpace(s));
        var joined = string.Join(", ", parts);
        return joined.Length == 0 ? null : joined;
    }

    private static string? DescribeLeg(Destination leg)
    {
        var lines = new List<string>();
        if (leg.TransportMode != TransportMode.Unspecified)
            lines.Add($"Transport: {leg.TransportMode}");
        if (!string.IsNullOrWhiteSpace(leg.LodgingName))
            lines.Add($"Lodging: {leg.LodgingName}");
        if (!string.IsNullOrWhiteSpace(leg.ConfirmationNumber))
            lines.Add($"Confirmation: {leg.ConfirmationNumber}");
        if (!string.IsNullOrWhiteSpace(leg.Notes))
            lines.Add(leg.Notes);
        return lines.Count == 0 ? null : string.Join("\n", lines);
    }

    private static string? DescribeTrip(Trip trip)
        => string.IsNullOrWhiteSpace(trip.Notes) ? null : trip.Notes;

    // RFC 5545 TEXT escaping: backslash, semicolon, comma, and newlines. Folds long
    // content lines at 75 octets with a CRLF + leading space per the spec.
    private static void AppendLine(StringBuilder sb, string line)
    {
        const int max = 75;
        if (line.Length <= max)
        {
            sb.Append(line).Append("\r\n");
            return;
        }

        var index = 0;
        var first = true;
        while (index < line.Length)
        {
            var take = Math.Min(first ? max : max - 1, line.Length - index);
            if (!first) sb.Append(' ');
            sb.Append(line, index, take);
            sb.Append("\r\n");
            index += take;
            first = false;
        }
    }

    private static string Escape(string value) => value
        .Replace("\\", "\\\\")
        .Replace(";", "\\;")
        .Replace(",", "\\,")
        .Replace("\r\n", "\\n")
        .Replace("\n", "\\n")
        .Replace("\r", "\\n");
}
