using Microsoft.AspNetCore.Identity;

namespace TravelTracker.Web.Data.Entities;

// Application user backed by ASP.NET Core Identity (string/GUID key).
// Roles (Employee | Manager | Admin) are managed via Identity roles, not a
// column here. Extra profile fields live alongside the Identity columns.
public class AppUser : IdentityUser
{
    public string DisplayName { get; set; } = string.Empty;

    // Home/base office for the traveler, shown in the "who's out" duty-of-care
    // view. Optional.
    public string? BaseLocation { get; set; }

    // IANA/Windows time zone id for the traveler's base (e.g. "America/New_York").
    // Stored for future scheduling use; itinerary legs are date-only today.
    public string? TimeZoneId { get; set; }

    public int? DepartmentId { get; set; }
    public Department? Department { get; set; }

    // Who approves this user's trips — the org relationship the M6 approval flow
    // will build on. Seated here now (self-referencing FK) so duty-of-care and M6
    // aren't a retrofit. Optional and NOT necessarily the direct manager. The
    // approval workflow itself (submit/approve/reject) is deferred to M6; this only
    // stores the relationship. Enforced acyclic + non-self via Domain/ApproverGraph.
    public string? ApproverId { get; set; }
    public AppUser? Approver { get; set; }

    // Non-null tags this row as evaluation/QA data seeded by EvalSeeder under the
    // given batch id. Null for every real user. Filtered-indexed so teardown can
    // delete an eval batch cheaply and real-world queries are unaffected. See
    // EvalSeeder / EvalTeardown.
    public string? EvalBatchId { get; set; }

    // True = the account may sign in. Set false to offboard a leaver without
    // deleting history (trip/expense FKs are Restrict, so hard-delete is blocked;
    // deactivation is the intended lifecycle). Enforced in AppSignInManager.
    public bool IsActive { get; set; } = true;

    // --- Traveler profile depth (captured once at onboarding; feeds M3/M7) ------
    // Optional throughout. Sensitive government identifiers (passport number, Known
    // Traveler Number) are stored ENCRYPTED at rest: these two columns hold Data
    // Protection ciphertext, never plaintext. Read/write them only through
    // ISensitiveFieldProtector (see Models/TravelerProfileInput). Everything else is
    // plaintext PII.

    // Travel documents.
    public string? PassportNumberProtected { get; set; }
    public DateOnly? PassportExpiry { get; set; }
    public string? Nationality { get; set; }
    // Known Traveler Number (covers TSA PreCheck / Global Entry).
    public string? KnownTravelerNumberProtected { get; set; }

    // Contact + emergency.
    public string? MobileNumber { get; set; }
    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactPhone { get; set; }

    // Loyalty + preferences — dormant until M7 booking, stored now.
    public string? FrequentFlyerNumbers { get; set; }
    public string? SeatPreference { get; set; }
    public string? MealPreference { get; set; }
    public string? HotelPreference { get; set; }

    // Defaults that stop per-trip retyping (default approver is ApproverId above).
    // Default cost center is FK-linked reference data (SetNull on delete).
    public int? DefaultCostCenterId { get; set; }
    public CostCenter? DefaultCostCenter { get; set; }
    public ReimbursementMethod ReimbursementMethod { get; set; } = ReimbursementMethod.Unspecified;
}
