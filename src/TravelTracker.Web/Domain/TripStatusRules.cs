using TravelTracker.Web.Data.Entities;

namespace TravelTracker.Web.Domain;

// Legal trip status transitions for the tracking-only lifecycle (no approval
// gate — approvals arrive in M6). Centralised so the UI and tests agree.
public static class TripStatusRules
{
    private static readonly Dictionary<TripStatus, TripStatus[]> Allowed = new()
    {
        [TripStatus.Draft]     = new[] { TripStatus.Planned, TripStatus.Cancelled },
        [TripStatus.Planned]   = new[] { TripStatus.Completed, TripStatus.Cancelled, TripStatus.Draft },
        [TripStatus.Completed] = new[] { TripStatus.Planned },
        [TripStatus.Cancelled] = new[] { TripStatus.Draft },
    };

    public static bool CanTransition(TripStatus from, TripStatus to) =>
        Allowed.TryGetValue(from, out var next) && Array.IndexOf(next, to) >= 0;

    public static IReadOnlyList<TripStatus> NextStates(TripStatus from) =>
        Allowed.TryGetValue(from, out var next) ? next : Array.Empty<TripStatus>();
}
