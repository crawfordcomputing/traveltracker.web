using System.ComponentModel.DataAnnotations;
using TravelTracker.Web.Data.Entities;
using TravelTracker.Web.Services;

namespace TravelTracker.Web.Models;

// The editable traveler-profile fields, shared by the self-service
// (/Account/Manage/Profile) and admin (Admin/Users/Edit) surfaces so the two can
// never drift. Passport number and KTN are handled as plaintext HERE (in memory,
// for the form) but persisted ENCRYPTED via ISensitiveFieldProtector — see
// FromUser / ApplyTo. Everything is optional.
public class TravelerProfileInput
{
    // --- Travel documents ---
    [Display(Name = "Passport number"), StringLength(64)]
    public string? PassportNumber { get; set; }

    [Display(Name = "Passport expiry"), DataType(DataType.Date)]
    public DateOnly? PassportExpiry { get; set; }

    [Display(Name = "Nationality"), StringLength(80)]
    public string? Nationality { get; set; }

    [Display(Name = "Known Traveler Number (TSA PreCheck / Global Entry)"), StringLength(64)]
    public string? KnownTravelerNumber { get; set; }

    // --- Contact + emergency ---
    [Display(Name = "Mobile number"), Phone, StringLength(40)]
    public string? MobileNumber { get; set; }

    [Display(Name = "Emergency contact name"), StringLength(120)]
    public string? EmergencyContactName { get; set; }

    [Display(Name = "Emergency contact phone"), Phone, StringLength(40)]
    public string? EmergencyContactPhone { get; set; }

    // --- Loyalty + preferences (dormant until M7) ---
    [Display(Name = "Frequent-flyer / loyalty numbers"), StringLength(1000)]
    public string? FrequentFlyerNumbers { get; set; }

    [Display(Name = "Seat preference"), StringLength(120)]
    public string? SeatPreference { get; set; }

    [Display(Name = "Meal preference"), StringLength(120)]
    public string? MealPreference { get; set; }

    [Display(Name = "Hotel preference"), StringLength(120)]
    public string? HotelPreference { get; set; }

    // --- Defaults (default approver is the user's ApproverId, set by an admin) ---
    // FK into the managed cost-center table; the picker is supplied by the page.
    [Display(Name = "Default cost center")]
    public int? DefaultCostCenterId { get; set; }

    [Display(Name = "Reimbursement method")]
    public ReimbursementMethod ReimbursementMethod { get; set; } = ReimbursementMethod.Unspecified;

    // Populate the form from a user, decrypting the protected identifiers for display.
    public static TravelerProfileInput FromUser(AppUser u, ISensitiveFieldProtector protector) => new()
    {
        PassportNumber = protector.Unprotect(u.PassportNumberProtected),
        PassportExpiry = u.PassportExpiry,
        Nationality = u.Nationality,
        KnownTravelerNumber = protector.Unprotect(u.KnownTravelerNumberProtected),
        MobileNumber = u.MobileNumber,
        EmergencyContactName = u.EmergencyContactName,
        EmergencyContactPhone = u.EmergencyContactPhone,
        FrequentFlyerNumbers = u.FrequentFlyerNumbers,
        SeatPreference = u.SeatPreference,
        MealPreference = u.MealPreference,
        HotelPreference = u.HotelPreference,
        DefaultCostCenterId = u.DefaultCostCenterId,
        ReimbursementMethod = u.ReimbursementMethod,
    };

    // Write the (trimmed) form values back onto a user, encrypting the sensitive
    // identifiers. Blank inputs normalise to null so we never store empty strings.
    public void ApplyTo(AppUser u, ISensitiveFieldProtector protector)
    {
        u.PassportNumberProtected = protector.Protect(Clean(PassportNumber));
        u.PassportExpiry = PassportExpiry;
        u.Nationality = Clean(Nationality);
        u.KnownTravelerNumberProtected = protector.Protect(Clean(KnownTravelerNumber));
        u.MobileNumber = Clean(MobileNumber);
        u.EmergencyContactName = Clean(EmergencyContactName);
        u.EmergencyContactPhone = Clean(EmergencyContactPhone);
        u.FrequentFlyerNumbers = Clean(FrequentFlyerNumbers);
        u.SeatPreference = Clean(SeatPreference);
        u.MealPreference = Clean(MealPreference);
        u.HotelPreference = Clean(HotelPreference);
        u.DefaultCostCenterId = DefaultCostCenterId;
        u.ReimbursementMethod = ReimbursementMethod;
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
