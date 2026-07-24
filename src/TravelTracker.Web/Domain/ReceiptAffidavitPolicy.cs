namespace TravelTracker.Web.Domain;

// Decides whether a receipt-less line needs a typed affidavit. Config-driven, not a
// DB table (the configurable ExpensePolicy stays in M3d): the Expenses:ReceiptRequired
// toggle + Expenses:ReceiptThreshold feed this. Pure — no I/O.
//
// The affidavit is an ESCAPE HATCH, not a block: when required, the user substantiates
// a missing receipt with a justification instead of being stopped. Above the threshold
// a receipt is expected but we still only advise (submission-blocking is deferred).
public enum ReceiptRequirement
{
    // Policy off, or a receipt is present: nothing to do.
    NotRequired,

    // Policy on, no receipt, amount at/under threshold: a typed affidavit substitutes.
    AffidavitNeeded,

    // Policy on, no receipt, amount OVER threshold: a receipt is expected. Advisory
    // in this slice (an affidavit still records intent); hard block lands in M3d.
    ReceiptExpected
}

public static class ReceiptAffidavitPolicy
{
    public static ReceiptRequirement Decision(
        bool hasReceipt, decimal baseAmount, bool receiptRequired, decimal threshold)
    {
        if (!receiptRequired || hasReceipt) return ReceiptRequirement.NotRequired;
        return baseAmount <= threshold
            ? ReceiptRequirement.AffidavitNeeded
            : ReceiptRequirement.ReceiptExpected;
    }

    // True when the policy wants *some* justification (affidavit) for this line and
    // none has been typed yet. Advisory flag for the UI banner.
    public static bool NeedsJustification(
        bool hasReceipt, decimal baseAmount, bool receiptRequired, decimal threshold, string? affidavit)
    {
        var decision = Decision(hasReceipt, baseAmount, receiptRequired, threshold);
        return decision != ReceiptRequirement.NotRequired && string.IsNullOrWhiteSpace(affidavit);
    }
}
