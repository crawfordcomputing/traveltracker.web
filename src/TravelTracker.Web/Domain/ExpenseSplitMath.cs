namespace TravelTracker.Web.Domain;

// Pure reconciliation helpers for same-trip category splits. A split set is valid
// only when its frozen BaseAmounts sum exactly to the parent's BaseAmount — the M3
// freeze theme applied to allocations. No DB, no I/O — unit testable in isolation.
public static class ExpenseSplitMath
{
    // How much of the parent is still unallocated (parent - sum). Positive = under-
    // allocated, negative = over-allocated, zero = reconciled. Drives the UI's
    // "X left to allocate" hint.
    public static decimal Remainder(decimal parentBaseAmount, IEnumerable<decimal> splitBaseAmounts)
        => parentBaseAmount - splitBaseAmounts.Sum();

    // True when the splits reconcile to the parent to the cent. An empty set never
    // reconciles a non-zero parent (there is nothing to allocate). A parent of 0
    // reconciles only to an empty set or all-zero splits.
    public static bool Reconciles(decimal parentBaseAmount, IEnumerable<decimal> splitBaseAmounts)
    {
        var list = splitBaseAmounts as IReadOnlyCollection<decimal> ?? splitBaseAmounts.ToList();
        if (list.Count == 0) return parentBaseAmount == 0m;
        return Remainder(parentBaseAmount, list) == 0m;
    }

    // Divide a parent amount into n cent-accurate parts. Every part is the floored
    // even share; the LAST part absorbs the rounding remainder so the set always
    // sums back to the parent exactly (e.g. 100.00 / 3 → 33.33, 33.33, 33.34).
    public static IReadOnlyList<decimal> DistributeEvenly(decimal parentBaseAmount, int n)
    {
        if (n <= 0) throw new ArgumentOutOfRangeException(nameof(n), "Need at least one part.");

        var cents = decimal.Round(parentBaseAmount, 2, MidpointRounding.AwayFromZero);
        var each = decimal.Round(cents / n, 2, MidpointRounding.ToZero);

        var parts = new decimal[n];
        decimal running = 0m;
        for (var i = 0; i < n - 1; i++)
        {
            parts[i] = each;
            running += each;
        }
        parts[n - 1] = cents - running; // last part squares the total
        return parts;
    }
}
