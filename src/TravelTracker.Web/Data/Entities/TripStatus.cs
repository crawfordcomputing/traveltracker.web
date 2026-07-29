namespace TravelTracker.Web.Data.Entities;

// Trip lifecycle. M6 adds a pre-approval gate in front of travel:
//   Draft     -> being planned, fully editable
//   Submitted -> sent to the traveler's approver, awaiting a decision
//   Approved  -> approver signed off; cleared for travel (the "confirmed/upcoming"
//                state — this is what Planned used to mean before the gate existed)
//   Rejected  -> approver sent it back; the traveler revises and resubmits
//   Completed -> travel finished
//   Cancelled -> called off
//
// Planned is a legacy value kept only so trips created before M6 still resolve and
// can move forward (it behaves like Approved in the transition rules). New trips
// never enter Planned — the approval gate produces Approved instead.
public enum TripStatus
{
    Draft = 0,
    Planned = 1,      // legacy (pre-M6); treated like Approved in TripStatusRules
    Completed = 2,
    Cancelled = 3,
    Submitted = 4,
    Approved = 5,
    Rejected = 6
}
