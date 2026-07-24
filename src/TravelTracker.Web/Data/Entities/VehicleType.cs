using System.ComponentModel.DataAnnotations;

namespace TravelTracker.Web.Data.Entities;

// The kind of vehicle a mileage rate applies to. Tax authorities publish
// different per-distance rates by vehicle (the IRS, for one, sets separate
// automobile / motorcycle figures), so the rate table and each entry key off
// this. Kept small and generic; values are stable — append, never renumber.
public enum VehicleType
{
    Car = 0,
    Motorcycle = 1,
    Van = 2,
    Truck = 3,
    Bicycle = 4,
    Other = 99
}
