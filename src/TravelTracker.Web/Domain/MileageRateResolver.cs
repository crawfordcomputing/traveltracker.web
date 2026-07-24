using TravelTracker.Web.Data.Entities;

namespace TravelTracker.Web.Domain;

// Pure resolver for the effective-dated rate table — no DB, no I/O. Given the
// candidate rates and a mileage date, returns the row that was IN EFFECT on that
// date: the one matching (jurisdiction, vehicle, unit) with the latest
// EffectiveDate that is on or before the date. This is the value the Create/Edit
// pages freeze onto the entry, so a rate added later never disturbs past mileage.
public static class MileageRateResolver
{
    public static MileageRate? Resolve(
        IEnumerable<MileageRate> rates,
        string jurisdiction,
        VehicleType vehicleType,
        DistanceUnit unit,
        DateOnly date)
    {
        MileageRate? best = null;
        foreach (var r in rates)
        {
            if (r.VehicleType != vehicleType || r.Unit != unit) continue;
            if (!string.Equals(r.Jurisdiction, jurisdiction, StringComparison.OrdinalIgnoreCase)) continue;
            if (r.EffectiveDate > date) continue; // not yet in effect on the mileage date

            // Keep the latest-effective row at or before the date. On a tie
            // (shouldn't happen — the key is unique) the higher Id wins for stability.
            if (best is null
                || r.EffectiveDate > best.EffectiveDate
                || (r.EffectiveDate == best.EffectiveDate && r.Id > best.Id))
            {
                best = r;
            }
        }
        return best;
    }
}
