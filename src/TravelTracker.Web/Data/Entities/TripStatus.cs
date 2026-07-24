namespace TravelTracker.Web.Data.Entities;

// Trip lifecycle for tracking-only M2 (no approval gate — approvals are M6).
//   Draft     -> being planned, fully editable
//   Planned   -> itinerary confirmed / upcoming
//   Completed -> travel finished
//   Cancelled -> called off
public enum TripStatus
{
    Draft = 0,
    Planned = 1,
    Completed = 2,
    Cancelled = 3
}
