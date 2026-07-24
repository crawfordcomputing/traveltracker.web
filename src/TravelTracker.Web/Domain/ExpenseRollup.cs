using TravelTracker.Web.Data.Entities;

namespace TravelTracker.Web.Domain;

// Pure per-trip expense rollup. Sums the FROZEN BaseAmount (never the original
// Amount, which may be in a foreign currency once multi-currency entry lands),
// splitting the reimbursable total from personal / non-reimbursable lines. No DB,
// no I/O — unit testable in isolation.
//
// Category splits: when a line has splits, its money is attributed to the SPLIT
// categories (each split's frozen BaseAmount), not the line's own Category. Because
// a split set reconciles to the parent's BaseAmount, the reimbursable/personal
// totals are unchanged — only the category breakdown gets finer. Callers must
// Include(e => e.Splits) or split lines collapse to their own category.
public static class ExpenseRollup
{
    public static ExpenseRollupResult Summarize(IEnumerable<Expense> expenses)
    {
        // Materialize once — the source is enumerated for both the category grouping
        // and the reimbursable/personal split.
        var lines = expenses as IReadOnlyList<Expense> ?? expenses.ToList();

        // Expand each line into one or more (category, amount) contributions: split
        // categories when present AND reconciled, otherwise the line's own category.
        // The reconcile guard means a half-finished split set (add-one-at-a-time
        // editing persists intermediate states) never drifts the trip totals.
        var contributions = lines.SelectMany(e =>
            e.Splits is { Count: > 0 }
                && ExpenseSplitMath.Reconciles(e.BaseAmount, e.Splits.Select(s => s.BaseAmount))
                ? e.Splits.Select(s => (s.Category, s.BaseAmount))
                : new[] { (e.Category, e.BaseAmount) });

        var byCategory = contributions
            .GroupBy(c => c.Category)
            .Select(g => new ExpenseRollupLine(g.Key, g.Sum(c => c.BaseAmount), g.Count()))
            .OrderByDescending(l => l.Total)
            .ThenBy(l => l.Category)
            .ToList();

        decimal reimbursable = 0m, personal = 0m;
        foreach (var e in lines)
        {
            if (e.IsPersonal) personal += e.BaseAmount;
            else reimbursable += e.BaseAmount;
        }

        return new ExpenseRollupResult(
            byCategory,
            Reimbursable: reimbursable,
            Personal: personal,
            Total: reimbursable + personal,
            Count: lines.Count);
    }
}

// One category's contribution to the rollup (BaseAmount summed across its lines).
public sealed record ExpenseRollupLine(ExpenseCategory Category, decimal Total, int Count);

// The whole-trip totals. Reimbursable excludes personal lines; Total is everything.
public sealed record ExpenseRollupResult(
    IReadOnlyList<ExpenseRollupLine> ByCategory,
    decimal Reimbursable,
    decimal Personal,
    decimal Total,
    int Count)
{
    public static readonly ExpenseRollupResult Empty =
        new(Array.Empty<ExpenseRollupLine>(), 0m, 0m, 0m, 0);
}
