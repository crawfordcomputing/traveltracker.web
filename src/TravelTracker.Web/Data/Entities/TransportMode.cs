namespace TravelTracker.Web.Data.Entities;

// How the traveler reaches a destination leg. Kept small and generic; the UI
// renders these as a dropdown.
public enum TransportMode
{
    Unspecified = 0,
    Flight = 1,
    Rail = 2,
    Car = 3,
    Bus = 4,
    Ferry = 5,
    Other = 6
}
