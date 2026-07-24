using TravelTracker.Web.Data.Entities;

namespace TravelTracker.Web.Domain;

// Date-integrity rules for trips and their destination legs. Centralised so the
// page handlers and unit tests agree (mirrors TripStatusRules).
//
// Design: the itinerary is the source of truth. A leg only has to be a valid
// interval (arrive <= depart); the trip window is then reconciled to always
// contain every leg (see ReconcileWindow). That makes "every leg sits inside the
// trip window" an invariant guaranteed by construction rather than a check that
// can reject legitimate edits.
public static class TripDateRules
{
    // A trip's window is valid when it doesn't end before it starts.
    public static bool IsValidTripRange(DateOnly start, DateOnly end) => end >= start;

    // Validates a single leg as a standalone interval. Returns null when valid,
    // otherwise a human-readable error suitable for ModelState.
    public static string? ValidateLeg(DateOnly arrive, DateOnly depart)
        => depart < arrive ? "Depart cannot be before arrive." : null;

    // Expands [start, end] so it contains every leg's arrive/depart. The manual
    // trip dates act as a floor (buffer days are preserved); legs can only push
    // the window outward, never shrink it. Returns the reconciled window.
    public static (DateOnly Start, DateOnly End) ReconcileWindow(
        DateOnly start, DateOnly end, IEnumerable<Destination> legs)
    {
        foreach (var leg in legs)
        {
            if (leg.ArriveDate < start) start = leg.ArriveDate;
            if (leg.DepartDate > end) end = leg.DepartDate;
        }
        return (start, end);
    }
}
