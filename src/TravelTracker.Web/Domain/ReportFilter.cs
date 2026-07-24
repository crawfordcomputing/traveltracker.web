using TravelTracker.Web.Data.Entities;

namespace TravelTracker.Web.Domain;

// Parses and validates the query-string filter shared by every M4 report:
// an optional date range (a trip is in range when its window OVERLAPS [From, To],
// consistent with "Who's out"), an optional status, and an optional traveler id.
// Pure and unit-testable — mirrors TripDateRules. Callers turn Error into ModelState.
//
// Defaults: an omitted From/To leaves that side of the range open. An inverted
// range (From > To) is a user error, surfaced rather than silently swapped, so the
// report never shows a confusingly empty result the user can't explain.
public sealed record ReportFilter(
    DateOnly? From,
    DateOnly? To,
    TripStatus? Status,
    string? TravelerId)
{
    public static readonly ReportFilter Empty = new(null, null, null, null);

    // Builds a filter from raw inputs. Returns (filter, error): error is non-null
    // only when the range is inverted; every other combination is valid.
    public static (ReportFilter Filter, string? Error) Create(
        DateOnly? from, DateOnly? to, TripStatus? status, string? travelerId)
    {
        var tid = string.IsNullOrWhiteSpace(travelerId) ? null : travelerId.Trim();
        var filter = new ReportFilter(from, to, status, tid);

        if (from is { } f && to is { } t && f > t)
            return (filter, "The 'from' date cannot be after the 'to' date.");

        return (filter, null);
    }

    // True when a trip's [start, end] window overlaps this filter's date range.
    // An open side (null) never excludes.
    public bool IncludesWindow(DateOnly start, DateOnly end)
    {
        if (From is { } f && end < f) return false;   // trip ends before the range
        if (To is { } t && start > t) return false;   // trip starts after the range
        return true;
    }
}
