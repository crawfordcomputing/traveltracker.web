using System.ComponentModel.DataAnnotations;

namespace TravelTracker.Web.Data.Entities;

// The kind of spend an expense line represents. Kept small and generic; the UI
// renders these as a dropdown. Values are stable — append new members, never
// renumber, so historical rows keep their meaning. M3d's ExpensePolicy keys its
// thresholds off this enum.
public enum ExpenseCategory
{
    Unspecified = 0,
    Airfare = 1,
    Lodging = 2,
    Meals = 3,

    [Display(Name = "Ground transport")]
    GroundTransport = 4,

    [Display(Name = "Car rental")]
    CarRental = 5,

    Fuel = 6,
    Parking = 7,
    Tolls = 8,
    Entertainment = 9,
    Conference = 10,
    Supplies = 11,
    Communication = 12,
    Other = 99
}
