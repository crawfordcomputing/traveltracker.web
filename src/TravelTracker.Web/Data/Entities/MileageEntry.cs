using System.ComponentModel.DataAnnotations;

namespace TravelTracker.Web.Data.Entities;

// A mileage reimbursement line on a trip. Design theme for M3: computed money
// values are FROZEN at entry, never recomputed live. Here that means the Rate
// (resolved from the effective-dated MileageRate table at this entry's Date) and
// the derived Amount are stored on the row. Later edits to the rate table leave
// existing entries untouched — the same guarantee Expense makes for FX.
public class MileageEntry
{
    public int Id { get; set; }

    public int TripId { get; set; }
    public Trip? Trip { get; set; }

    [DataType(DataType.Date)]
    public DateOnly Date { get; set; }

    // Distance for a single leg/one-way, as entered, in Unit. Round-trip and
    // commute-deduction adjustments are applied by MileageMath, not baked in here,
    // so the raw input stays visible and editable.
    [Range(0, 100_000)]
    public decimal Distance { get; set; }

    public DistanceUnit Unit { get; set; } = DistanceUnit.Miles;

    // Doubles the entered Distance (there and back) before the commute deduction.
    [Display(Name = "Round trip")]
    public bool IsRoundTrip { get; set; }

    // Distance to subtract for a normal, non-reimbursable commute (in Unit).
    // Floored at zero billable by MileageMath so it can never go negative.
    [Range(0, 100_000)]
    [Display(Name = "Commute deduction")]
    public decimal CommuteDeduction { get; set; }

    // --- Frozen at entry (the M3 freeze) ------------------------------------
    // Rate per unit, captured from the MileageRate in effect at Date.
    public decimal Rate { get; set; }

    // Frozen billable amount = BillableDistance * Rate, rounded to 2dp. Stored so
    // reports never recompute against a moving rate table.
    public decimal Amount { get; set; }

    // Snapshot of which rate was applied, for the audit trail — the entry stays
    // self-describing even if the rate row is later edited or removed.
    [StringLength(60)]
    public string Jurisdiction { get; set; } = "US";

    [Display(Name = "Vehicle type")]
    public VehicleType VehicleType { get; set; } = VehicleType.Car;

    // Nullable audit link to the source rate row. SetNull on delete: losing the
    // rate row must not erase the entry — the frozen Rate/Amount stand on their own.
    public int? RateId { get; set; }
    public MileageRate? MileageRate { get; set; }
    // ------------------------------------------------------------------------

    [StringLength(300)]
    public string? Purpose { get; set; }

    // Audit parity with Trip/Expense: an arranger/manager may enter on behalf of
    // the traveler.
    public string? CreatedById { get; set; }
    public AppUser? CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Ordered stops (A→B→C), owned by the entry.
    public List<MileageWaypoint> Waypoints { get; set; } = new();
}
