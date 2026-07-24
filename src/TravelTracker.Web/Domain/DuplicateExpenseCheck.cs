using TravelTracker.Web.Data.Entities;

namespace TravelTracker.Web.Domain;

// Advisory duplicate detection: flags existing lines that match a candidate on
// amount + date + vendor. NEVER blocks a save (consistent with ExpensePolicyCheck);
// the UI just shows a "possible duplicate" banner so the user can confirm. Pure —
// no DB, no I/O.
public static class DuplicateExpenseCheck
{
    // Existing lines that look like duplicates of the candidate. Matching rule:
    //   Amount equal, Date equal, and Vendor equal (case-insensitive, trimmed).
    // A null/blank vendor never matches (too weak a signal to flag on its own).
    // The candidate itself is excluded by Id so editing a line doesn't self-flag.
    public static IReadOnlyList<Expense> FindMatches(Expense candidate, IEnumerable<Expense> existing)
    {
        var vendor = Normalize(candidate.Vendor);
        if (vendor is null) return Array.Empty<Expense>();

        return existing
            .Where(e => e.Id != candidate.Id
                        && e.Amount == candidate.Amount
                        && e.Date == candidate.Date
                        && string.Equals(Normalize(e.Vendor), vendor, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    // Convenience for the page: is there at least one match?
    public static bool HasMatch(Expense candidate, IEnumerable<Expense> existing)
        => FindMatches(candidate, existing).Count > 0;

    // Trim only; case-insensitivity is handled at comparison time.
    private static string? Normalize(string? vendor)
        => string.IsNullOrWhiteSpace(vendor) ? null : vendor.Trim();
}
