using TravelTracker.Web.Data.Entities;

namespace TravelTracker.Web.Domain;

// Who may drive the trip lifecycle to where. M6 splits the actors: the traveler
// (owner, or someone acting on their behalf) drives the tracking lifecycle;
// the approver only decides on a Submitted trip. Centralised so the UI, handlers
// and tests agree.
public enum TripActor
{
    // The trip owner or a delegate acting on their behalf (arranger/manager/admin
    // with access). Drives submit/withdraw/revise/complete/cancel/reopen.
    Traveler,
    // The traveler's assigned approver, deciding a Submitted trip (approve/reject).
    Approver
}

// A single legal move: the target status, who may perform it, and the verb the UI
// shows on the button.
public readonly record struct TripTransition(TripStatus To, TripActor Actor, string Verb);

public static class TripStatusRules
{
    // Legacy Planned behaves exactly like Approved so pre-M6 trips can still move.
    private static TripStatus Canonical(TripStatus s) =>
        s == TripStatus.Planned ? TripStatus.Approved : s;

    // Advisory (NON-BLOCKING) notice for the cost-entry pages (Expenses/Mileage
    // Create). Returns null when the trip is "cleared for costs" — Approved or
    // Completed, and legacy Planned via Canonical — so no banner shows. For every
    // other status it returns a short, status-specific nudge. Saving is NEVER
    // prevented; this mirrors the app's existing advisory signals (over-guideline
    // badge, duplicate check). See ADR-0002.
    public static string? CostEntryWarning(TripStatus status) => Canonical(status) switch
    {
        TripStatus.Approved or TripStatus.Completed => null,
        TripStatus.Submitted => "This trip is still awaiting approval. Adding costs now is fine "
            + "and expected. Prebooked travel (flights, hotels), other pre-trip expenses, and "
            + "spend that piles up while approval is slow all belong here. They'll simply sit "
            + "against the trip until it's approved.",
        TripStatus.Rejected => "This trip was rejected. Consider revising and resubmitting "
            + "before logging costs against it.",
        TripStatus.Cancelled => "This trip is cancelled. Costs added here won't belong to an "
            + "active trip.",
        _ => "This trip hasn't been approved yet. Adding costs now is fine and expected. "
            + "Prebooked travel (flights, hotels), other pre-trip expenses, and spend incurred "
            + "while waiting on a slow approval all belong here. The trip should still be "
            + "submitted and approved when you can.", // Draft
    };

    private static readonly Dictionary<TripStatus, TripTransition[]> Allowed = new()
    {
        [TripStatus.Draft] = new[]
        {
            new TripTransition(TripStatus.Submitted, TripActor.Traveler, "Submit for approval"),
            new TripTransition(TripStatus.Cancelled, TripActor.Traveler, "Cancel trip"),
        },
        [TripStatus.Submitted] = new[]
        {
            new TripTransition(TripStatus.Approved, TripActor.Approver, "Approve"),
            new TripTransition(TripStatus.Rejected, TripActor.Approver, "Reject"),
            new TripTransition(TripStatus.Draft,     TripActor.Traveler, "Withdraw"),
        },
        [TripStatus.Approved] = new[]
        {
            new TripTransition(TripStatus.Completed, TripActor.Traveler, "Mark completed"),
            new TripTransition(TripStatus.Cancelled, TripActor.Traveler, "Cancel trip"),
        },
        [TripStatus.Rejected] = new[]
        {
            new TripTransition(TripStatus.Draft,     TripActor.Traveler, "Revise"),
            new TripTransition(TripStatus.Cancelled, TripActor.Traveler, "Cancel trip"),
        },
        [TripStatus.Completed] = new[]
        {
            new TripTransition(TripStatus.Approved, TripActor.Traveler, "Reopen"),
        },
        [TripStatus.Cancelled] = new[]
        {
            new TripTransition(TripStatus.Draft, TripActor.Traveler, "Reopen"),
        },
    };

    // Every legal move out of a status (any actor).
    public static IReadOnlyList<TripTransition> Transitions(TripStatus from) =>
        Allowed.TryGetValue(Canonical(from), out var t) ? t : Array.Empty<TripTransition>();

    // The moves a given actor may perform from here — drives which buttons to show.
    public static IReadOnlyList<TripTransition> TransitionsFor(TripStatus from, TripActor actor) =>
        Transitions(from).Where(t => t.Actor == actor).ToList();

    // True if `actor` may move a trip from -> to.
    public static bool CanTransition(TripStatus from, TripStatus to, TripActor actor) =>
        Transitions(from).Any(t => t.To == to && t.Actor == actor);

    // Actor-agnostic overload: is from -> to legal for anyone?
    public static bool CanTransition(TripStatus from, TripStatus to) =>
        Transitions(from).Any(t => t.To == to);

    // Target statuses reachable from here (any actor). Kept for callers/tests that
    // only care about the shape of the graph.
    public static IReadOnlyList<TripStatus> NextStates(TripStatus from) =>
        Transitions(from).Select(t => t.To).ToList();
}
