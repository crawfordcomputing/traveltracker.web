using System.ComponentModel.DataAnnotations;

namespace TravelTracker.Web.Data.Entities;

// Unit a mileage distance and its rate are expressed in. A rate and the entry
// that freezes it always share a unit — the resolver matches on it, so a US
// per-mile rate can never be applied to a distance entered in kilometres.
// Values are stable — append, never renumber.
public enum DistanceUnit
{
    Miles = 0,
    Kilometers = 1
}
