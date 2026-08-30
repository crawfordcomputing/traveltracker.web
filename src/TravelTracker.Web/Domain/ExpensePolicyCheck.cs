using TravelTracker.Web.Data.Entities;

namespace TravelTracker.Web.Domain;

// ADVISORY per-category spend flag. The per-category soft caps now come from the
// configurable ExpensePolicy table (managed under /Admin/ExpensePolicies), not a
// hardcoded dictionary. See ADR-0003.
//
// This stays a PURE helper: callers load the caps once and pass them in, matching
// the app's "domain logic is static + pure, pages load the data" convention. It
// NEVER blocks entry or submission — it only drives the over-guideline badge.
public static class ExpensePolicyCheck
{
    // Builds the (category -> cap) lookup this helper expects from ExpensePolicy
    // rows. Categories with no row are absent, i.e. uncapped.
    public static IReadOnlyDictionary<ExpenseCategory, decimal> CapsFrom(
        IEnumerable<ExpensePolicy> policies) =>
        policies.ToDictionary(p => p.Category, p => p.CapAmount);

    // Shared empty lookup for callers that have no policies configured.
    public static readonly IReadOnlyDictionary<ExpenseCategory, decimal> NoCaps =
        new Dictionary<ExpenseCategory, decimal>();

    // The soft cap for a category, or null when none applies.
    public static decimal? CapFor(
        ExpenseCategory category, IReadOnlyDictionary<ExpenseCategory, decimal> caps) =>
        caps.TryGetValue(category, out var cap) ? cap : null;

    // True when the line's frozen BaseAmount exceeds its category cap. Advisory only.
    public static bool IsOverCap(
        Expense expense, IReadOnlyDictionary<ExpenseCategory, decimal> caps)
    {
        var cap = CapFor(expense.Category, caps);
        return cap is not null && expense.BaseAmount > cap.Value;
    }

    // A short advisory message, or null when within policy / uncapped.
    public static string? Flag(
        Expense expense, IReadOnlyDictionary<ExpenseCategory, decimal> caps)
    {
        var cap = CapFor(expense.Category, caps);
        if (cap is null || expense.BaseAmount <= cap.Value) return null;
        return $"Over the {expense.Category} guideline of {cap.Value:C0}";
    }
}
