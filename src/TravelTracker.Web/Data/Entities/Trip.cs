using System.ComponentModel.DataAnnotations;

namespace TravelTracker.Web.Data.Entities;

// A business trip owned by a traveler. Approvals are deferred to M6; for now a
// Trip only moves through the tracking lifecycle defined in TripStatus.
public class Trip
{
    public int Id { get; set; }

    // The employee the trip is for. May differ from CreatedById when a
    // Manager/Admin books a trip on someone else's behalf.
    public string TravelerId { get; set; } = string.Empty;
    public AppUser? Traveler { get; set; }

    [Required, StringLength(200)]
    public string Purpose { get; set; } = string.Empty;

    public TripStatus Status { get; set; } = TripStatus.Draft;

    [Display(Name = "Trip type")]
    public TripType Type { get; set; } = TripType.Unspecified;

    // Project code / cost center are FK-linked reference data (managed under
    // /Reference). SetNull on delete so retiring a code never erases trip history.
    [Display(Name = "Project code")]
    public int? ProjectCodeId { get; set; }
    public ProjectCode? ProjectCode { get; set; }

    [Display(Name = "Cost center")]
    public int? CostCenterId { get; set; }
    public CostCenter? CostCenter { get; set; }

    [Display(Name = "Start date")]
    public DateOnly StartDate { get; set; }

    [Display(Name = "End date")]
    public DateOnly EndDate { get; set; }

    [StringLength(1000)]
    public string? Notes { get; set; }

    // Captured when the trip is moved to Cancelled, for the audit trail.
    [StringLength(300)]
    public string? CancellationReason { get; set; }

    // Audit trail for "on behalf of" creation.
    public string? CreatedById { get; set; }
    public AppUser? CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<Destination> Destinations { get; set; } = new();

    // Approval decision history (submit/approve/reject), newest interesting last.
    // Owned by the trip; see AppDbContext. Empty until the trip is first submitted.
    public List<Approval> Approvals { get; set; } = new();
}
