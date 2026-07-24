using TravelTracker.Web.Data.Entities;

namespace TravelTracker.Web.Domain;

// Detects when a traveler is booked in two places at once. This is advisory
// (a warning, not a hard block) — a traveler may legitimately have overlapping
// draft plans, but it usually signals a double-booking worth a second look.
public static class TripOverlap
{
    // Two inclusive date windows overlap when each starts on or before the other
    // ends.
    public static bool Overlaps(DateOnly aStart, DateOnly aEnd, DateOnly bStart, DateOnly bEnd)
        => aStart <= bEnd && bStart <= aEnd;

    // Returns the traveler's other trips whose window overlaps [start, end].
    // Cancelled trips and the trip itself (excludeTripId) are ignored.
    public static IEnumerable<Trip> FindConflicts(
        IEnumerable<Trip> travelerTrips, DateOnly start, DateOnly end, int? excludeTripId = null)
        => travelerTrips.Where(t =>
            t.Id != excludeTripId &&
            t.Status != TripStatus.Cancelled &&
            Overlaps(start, end, t.StartDate, t.EndDate));
}
