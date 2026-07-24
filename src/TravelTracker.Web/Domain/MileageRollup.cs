using TravelTracker.Web.Data.Entities;

namespace TravelTracker.Web.Domain;

// Pure per-trip mileage rollup — sums the FROZEN Amount and the billable distance
// across a trip's mileage entries. Kept separate from ExpenseRollup because the
// two live in different units (money vs distance) and are shown as distinct
// sections on the trip; M4 reporting combines the money totals.
public static class MileageRollup
{
    public static MileageRollupResult Summarize(IEnumerable<MileageEntry> entries)
    {
        var lines = entries as IReadOnlyList<MileageEntry> ?? entries.ToList();
        if (lines.Count == 0) return MileageRollupResult.Empty;

        decimal amount = 0m, milesBillable = 0m, kmBillable = 0m;
        foreach (var e in lines)
        {
            amount += e.Amount;
            var billable = MileageMath.BillableDistance(e.Distance, e.IsRoundTrip, e.CommuteDeduction);
            if (e.Unit == DistanceUnit.Kilometers) kmBillable += billable;
            else milesBillable += billable;
        }

        return new MileageRollupResult(
            Amount: amount,
            MilesBillable: milesBillable,
            KilometersBillable: kmBillable,
            Count: lines.Count);
    }
}

// Trip mileage totals. Distance is split by unit so mixed miles/km entries never
// silently add together; Amount is always in base currency (frozen per entry).
public sealed record MileageRollupResult(
    decimal Amount,
    decimal MilesBillable,
    decimal KilometersBillable,
    int Count)
{
    public static readonly MileageRollupResult Empty = new(0m, 0m, 0m, 0);
}
