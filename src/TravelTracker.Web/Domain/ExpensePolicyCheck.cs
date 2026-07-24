using TravelTracker.Web.Data.Entities;

namespace TravelTracker.Web.Domain;

// MINIMAL, ADVISORY per-category spend flag for M3a's "simple policy-limit
// flagging." A tiny built-in threshold table (base currency) drives an over-limit
// badge on the trip's expense list. It NEVER blocks submission.
//
// This is deliberately a placeholder. M3d replaces it with the configurable
// ExpensePolicy table (per-category thresholds, receipt-required, itemization
// rules, submission blocking). Keep the shape small so swapping it out is cheap.
public static class ExpensePolicyCheck
{
    // Per-line soft caps in base currency. Categories absent here are unlimited.
    private static readonly Dictionary<ExpenseCategory, decimal> DefaultCaps =
        new()
        {
            [ExpenseCategory.Meals] = 75m,
            [ExpenseCategory.Lodging] = 350m,
            [ExpenseCategory.Entertainment] = 150m,
            [ExpenseCategory.GroundTransport] = 100m,
        };

    // The soft cap for a category, or null when none applies.
    public static decimal? CapFor(ExpenseCategory category) =>
        DefaultCaps.TryGetValue(category, out var cap) ? cap : null;

    // True when the line's frozen BaseAmount exceeds its category cap. Advisory only.
    public static bool IsOverCap(Expense expense)
    {
        var cap = CapFor(expense.Category);
        return cap is not null && expense.BaseAmount > cap.Value;
    }

    // A short advisory message, or null when within policy / uncapped.
    public static string? Flag(Expense expense)
    {
        var cap = CapFor(expense.Category);
        if (cap is null || expense.BaseAmount <= cap.Value) return null;
        return $"Over the {expense.Category} guideline of {cap.Value:C0}";
    }
}
