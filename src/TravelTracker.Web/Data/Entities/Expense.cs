using System.ComponentModel.DataAnnotations;

namespace TravelTracker.Web.Data.Entities;

// A single expense line on a trip. Design theme for M3: computed money values are
// FROZEN at entry, never recomputed live, so M4 reporting and M7 accounting export
// inherit stable numbers.
//
// M3a is single-(base-)currency in the UI, but the currency-freezing columns
// (Currency / FxRate / FxRateDate / BaseAmount) exist now so the multi-currency
// entry UI in M3c is a UI change only, not another migration on this hot table.
// In M3a: Currency = base, FxRate = 1, FxRateDate = null, BaseAmount = Amount.
public class Expense
{
    public int Id { get; set; }

    public int TripId { get; set; }
    public Trip? Trip { get; set; }

    public ExpenseCategory Category { get; set; } = ExpenseCategory.Unspecified;

    [DataType(DataType.Date)]
    public DateOnly Date { get; set; }

    [StringLength(160)]
    public string? Vendor { get; set; }

    [StringLength(500)]
    public string? Description { get; set; }

    // The amount exactly as entered, in the original Currency.
    [Range(0, 1_000_000)]
    public decimal Amount { get; set; }

    // Frozen currency triplet (schema now, UI in M3c). ISO 4217 alpha-3.
    [Required, StringLength(3)]
    public string Currency { get; set; } = "USD";

    // Units of base currency per unit of Currency, captured at the expense date.
    // 1 for a base-currency expense.
    public decimal FxRate { get; set; } = 1m;

    // The date the FX rate was captured/effective (null when FxRate == 1).
    public DateOnly? FxRateDate { get; set; }

    // Frozen base-currency value = Amount * FxRate, stored so reports never
    // recompute against a moving rate.
    public decimal BaseAmount { get; set; }

    // Personal / non-reimbursable: nets out of amount owed but still reconciles
    // against the card feed in M7.
    [Display(Name = "Personal / non-reimbursable")]
    public bool IsPersonal { get; set; }

    // Meals & Entertainment substantiation (IRS-style): who was present and the
    // business reason. Surfaced/prompted only for those categories; never a hard
    // gate in this slice (submission-blocking rules are deferred to M3d).
    [StringLength(500)]
    public string? Attendees { get; set; }

    [StringLength(500)]
    [Display(Name = "Business purpose")]
    public string? BusinessPurpose { get; set; }

    // Lightweight reimbursement status, INDEPENDENT of the trip's status. The full
    // approval flow lives in M6; this is just a paid/not-paid marker + date so
    // finance can track payout without moving the whole trip.
    public bool Reimbursed { get; set; }

    [DataType(DataType.Date)]
    [Display(Name = "Reimbursed on")]
    public DateOnly? ReimbursedDate { get; set; }

    // Typed justification when the receipt-required policy is on and this line has
    // no receipt (under the affidavit threshold). Advisory escape hatch, not a block.
    [StringLength(500)]
    [Display(Name = "Missing-receipt affidavit")]
    public string? MissingReceiptAffidavit { get; set; }

    // Same-trip category splits. When present, rollups expand these instead of the
    // line's own Category; their frozen BaseAmounts must reconcile to this line's
    // BaseAmount (enforced in the UI via ExpenseSplitMath).
    public List<ExpenseSplit> Splits { get; set; } = new();

    // Opaque storage key returned by IReceiptStorage — NOT a public/user path.
    // Served only through the authorized Receipt handler.
    [StringLength(260)]
    public string? ReceiptPath { get; set; }

    // Audit parity with Trip: an arranger/manager may enter an expense on behalf
    // of the traveler.
    public string? CreatedById { get; set; }
    public AppUser? CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
