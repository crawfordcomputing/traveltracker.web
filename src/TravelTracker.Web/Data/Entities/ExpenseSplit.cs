using System.ComponentModel.DataAnnotations;

namespace TravelTracker.Web.Data.Entities;

// One allocation of a parent Expense to a different category, on the SAME trip.
// (Cross-trip allocation is deferred.) Splits carry their own frozen BaseAmount;
// the set must reconcile to the parent's BaseAmount to the cent — enforced in the
// UI via Domain/ExpenseSplitMath, mirroring the M3 freeze-at-entry theme.
//
// A split does NOT re-run FX: it inherits the parent's currency/rate, so we only
// store the original Amount and the frozen BaseAmount.
public class ExpenseSplit
{
    public int Id { get; set; }

    public int ExpenseId { get; set; }
    public Expense? Expense { get; set; }

    public ExpenseCategory Category { get; set; } = ExpenseCategory.Unspecified;

    // As entered, in the parent's original currency.
    [Range(0, 1_000_000)]
    public decimal Amount { get; set; }

    // Frozen base-currency value = Amount * parent.FxRate, captured when the split
    // is saved so reports never recompute against a moving rate.
    public decimal BaseAmount { get; set; }

    [StringLength(500)]
    public string? Description { get; set; }
}
