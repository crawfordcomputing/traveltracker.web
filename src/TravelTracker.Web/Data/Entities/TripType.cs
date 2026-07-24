namespace TravelTracker.Web.Data.Entities;

// Business purpose category for a trip. Feeds M4 reporting rollups. Rendered as
// a dropdown; Unspecified is the default so it never forces a choice.
public enum TripType
{
    Unspecified = 0,
    Business = 1,
    Conference = 2,
    Training = 3,
    ClientVisit = 4,
    Internal = 5,
    Other = 6
}
