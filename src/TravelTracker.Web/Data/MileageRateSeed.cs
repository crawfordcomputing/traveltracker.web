using TravelTracker.Web.Data.Entities;

namespace TravelTracker.Web.Data;

// Starter rows for the effective-dated mileage rate table: the IRS standard
// business mileage rates for US automobiles. Seeded idempotently at startup (like
// Countries / UsStates) so a fresh install can compute mileage immediately; admins
// add later years / other jurisdictions through the Admin/MileageRates UI.
//
// Note the two 2026 rows: the IRS raised the rate mid-year (72.5¢ → 76¢ effective
// Jul 1, 2026), which is exactly why entries freeze the rate in effect at their
// date rather than a single per-year figure.
public static class MileageRateSeed
{
    public static readonly (DateOnly EffectiveDate, decimal Rate, string Notes)[] UsBusinessAuto =
    {
        (new DateOnly(2023, 1, 1), 0.655m, "IRS standard business mileage rate (2023)"),
        (new DateOnly(2024, 1, 1), 0.670m, "IRS standard business mileage rate (2024)"),
        (new DateOnly(2025, 1, 1), 0.700m, "IRS standard business mileage rate (2025)"),
        (new DateOnly(2026, 1, 1), 0.725m, "IRS standard business mileage rate (2026, H1)"),
        (new DateOnly(2026, 7, 1), 0.760m, "IRS standard business mileage rate (2026, H2 mid-year increase)"),
    };

    public static IEnumerable<MileageRate> BuildAll() =>
        UsBusinessAuto.Select(r => new MileageRate
        {
            EffectiveDate = r.EffectiveDate,
            Jurisdiction = "US",
            VehicleType = VehicleType.Car,
            Unit = DistanceUnit.Miles,
            Rate = r.Rate,
            Notes = r.Notes,
        });
}
