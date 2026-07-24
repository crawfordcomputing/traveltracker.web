using TravelTracker.Web.Data.Entities;

namespace TravelTracker.Web.Domain;

// Which categories should prompt for attendees + business purpose. Meals and
// Entertainment carry an IRS-style substantiation expectation (who was there, why).
// This drives a SOFT UI prompt only — submission-blocking itemization rules are
// deferred to M3d, so this never fails validation on its own.
public static class MealAttendeeRule
{
    public static bool RequiresAttendees(ExpenseCategory category)
        => category is ExpenseCategory.Meals or ExpenseCategory.Entertainment;

    // True when the category expects substantiation but the line hasn't supplied
    // either field yet — used to render a gentle reminder, not to block.
    public static bool IsIncomplete(ExpenseCategory category, string? attendees, string? businessPurpose)
        => RequiresAttendees(category)
           && (string.IsNullOrWhiteSpace(attendees) || string.IsNullOrWhiteSpace(businessPurpose));
}
