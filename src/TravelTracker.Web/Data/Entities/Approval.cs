using System.ComponentModel.DataAnnotations;

namespace TravelTracker.Web.Data.Entities;

// The decision an approver recorded on a Submitted trip.
public enum ApprovalDecision
{
    Approved = 0,
    Rejected = 1,
    // Not an approver decision: recorded when a traveler reopens an already-Approved
    // trip to revise its itinerary, invalidating the prior sign-off. Stored in the
    // same history log so the trail reads approve -> reopen -> resubmit -> re-approve.
    // ApproverId holds the acting user (traveler/arranger), not necessarily an approver.
    Reopened = 2
}

// One immutable row per approval decision, forming the trip's approval history.
// Written by the submit/approve/reject handlers; never edited after the fact.
// A trip can accumulate several rows over its life (submit -> reject -> revise ->
// resubmit -> approve), so this is a one-to-many audit log, not a single verdict.
public class Approval
{
    public int Id { get; set; }

    public int TripId { get; set; }
    public Trip? Trip { get; set; }

    // The user who made the decision (the traveler's approver at decision time).
    public string ApproverId { get; set; } = string.Empty;
    public AppUser? Approver { get; set; }

    public ApprovalDecision Decision { get; set; }

    // Optional on approve; required on reject (enforced in the handler) so the
    // traveler knows what to change.
    [StringLength(1000)]
    public string? Comment { get; set; }

    public DateTimeOffset DecidedAt { get; set; } = DateTimeOffset.UtcNow;
}
