using System.ComponentModel.DataAnnotations;

namespace TravelTracker.Web.Data.Entities;

// A per-distance reimbursement rate that takes effect on EffectiveDate and stays
// current until a later-dated row for the same (Jurisdiction, VehicleType, Unit)
// supersedes it. This is the historical rate table: a mileage entry freezes the
// rate in effect at ITS date, so past entries never move when a new rate lands.
//
// Real example this shape captures: the IRS 2026 US business automobile rate
// changed mid-year (72.5¢/mi from Jan 1, 76¢/mi from Jul 1) — two rows, same key,
// different EffectiveDate.
public class MileageRate
{
    public int Id { get; set; }

    // Applies from this date forward (inclusive) until the next later row wins.
    [Display(Name = "Effective date")]
    public DateOnly EffectiveDate { get; set; }

    // Free-form jurisdiction tag (e.g. "US", "UK", a state). Matched exactly
    // (case-insensitively) by the resolver, so keep values consistent.
    [Required, StringLength(60)]
    public string Jurisdiction { get; set; } = "US";

    [Display(Name = "Vehicle type")]
    public VehicleType VehicleType { get; set; } = VehicleType.Car;

    public DistanceUnit Unit { get; set; } = DistanceUnit.Miles;

    // Reimbursement per unit of distance (e.g. 0.700000 = 70¢/mile).
    [Range(0, 1000)]
    public decimal Rate { get; set; }

    [StringLength(200)]
    public string? Notes { get; set; }
}
