using TravelTracker.Web.Data.Entities;

namespace TravelTracker.Web.Domain;

// Pure builder for "clone last trip's expenses". Copies the substantive fields of a
// source line into a NEW line for a target trip, rebasing the date. Deliberately
// does NOT carry over receipt-bound or lifecycle state — a cloned line starts fresh:
//   - no ReceiptPath (receipts are per-actual-transaction)
//   - no MissingReceiptAffidavit (re-justify if still needed)
//   - not Reimbursed (a fresh claim)
// Frozen money (Amount/Currency/FxRate/BaseAmount) and same-trip splits are copied
// verbatim so the clone reconciles exactly like its source. No DB, no I/O.
public static class ExpenseClone
{
    public static Expense From(
        Expense source, int targetTripId, string? createdById, DateOnly date, DateTimeOffset now)
    {
        return new Expense
        {
            TripId = targetTripId,
            Category = source.Category,
            Date = date,
            Vendor = source.Vendor,
            Description = source.Description,
            Amount = source.Amount,
            Currency = source.Currency,
            FxRate = source.FxRate,
            FxRateDate = source.FxRateDate,
            BaseAmount = source.BaseAmount,
            IsPersonal = source.IsPersonal,
            Attendees = source.Attendees,
            BusinessPurpose = source.BusinessPurpose,
            // Intentionally NOT copied: ReceiptPath, MissingReceiptAffidavit,
            // Reimbursed, ReimbursedDate.
            CreatedById = createdById,
            CreatedAt = now,
            Splits = source.Splits.Select(s => new ExpenseSplit
            {
                Category = s.Category,
                Amount = s.Amount,
                BaseAmount = s.BaseAmount,
                Description = s.Description
            }).ToList()
        };
    }
}
