using System.ComponentModel.DataAnnotations;

namespace TravelTracker.Web.Data.Entities;

// A configurable, per-category spend guideline in base currency. Replaces the
// hardcoded caps that ExpensePolicyCheck used to carry (see ADR-0003). One row per
// ExpenseCategory (unique). A category with no row is uncapped, exactly as before.
//
// ADVISORY ONLY: the cap drives the "Over guideline" badge on the trip's expense
// list. It never blocks entry or submission, consistent with the app's other soft
// signals (duplicate check, missing-receipt affidavit, cost-entry approval warning).
//
// The cap is a current value, not effective-dated: editing it changes the guideline
// going forward. Nothing is frozen onto past expenses, so the badge simply
// re-evaluates against the latest cap. Admins manage these under
// /Admin/ExpensePolicies.
public class ExpensePolicy
{
    public int Id { get; set; }

    // The category this guideline applies to. Unique across the table.
    public ExpenseCategory Category { get; set; }

    // Soft cap in base currency. A line whose BaseAmount exceeds this is flagged.
    [Range(0, 1_000_000)]
    [Display(Name = "Cap amount")]
    public decimal CapAmount { get; set; }

    // Optional admin note (e.g. "per-night lodging guideline").
    [StringLength(200)]
    public string? Notes { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
