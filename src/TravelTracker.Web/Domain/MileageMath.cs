namespace TravelTracker.Web.Domain;

// Pure mileage arithmetic — no DB, no I/O, fully unit-testable. Splitting this
// out (like TripDateRules / ExpenseRollup) keeps the freeze logic in one place so
// the Create/Edit pages and the tests compute amounts identically.
public static class MileageMath
{
    // Billable distance after applying the round-trip toggle and subtracting a
    // non-reimbursable commute. Never negative: an over-large commute deduction
    // floors the result at zero rather than producing a credit.
    public static decimal BillableDistance(decimal distance, bool isRoundTrip, decimal commuteDeduction)
    {
        var gross = isRoundTrip ? distance * 2m : distance;
        var net = gross - commuteDeduction;
        return net > 0m ? net : 0m;
    }

    // Frozen reimbursement amount = billable distance * rate, rounded to 2dp
    // (banker's rounding avoided — MidpointRounding.AwayFromZero matches how money
    // is conventionally rounded on an expense line).
    public static decimal Amount(decimal billableDistance, decimal rate) =>
        Math.Round(billableDistance * rate, 2, MidpointRounding.AwayFromZero);
}
