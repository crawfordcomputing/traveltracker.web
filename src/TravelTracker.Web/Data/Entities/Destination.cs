using System.ComponentModel.DataAnnotations;

namespace TravelTracker.Web.Data.Entities;

// One leg of a trip's itinerary. A trip has one or more destinations ordered by
// Sequence, supporting multi-leg travel.
public class Destination
{
    public int Id { get; set; }

    public int TripId { get; set; }
    public Trip? Trip { get; set; }

    [Required, StringLength(120)]
    public string City { get; set; } = string.Empty;

    // Free-text state/province. Populated from the UsState dropdown when Country
    // is the United States; optional for other countries.
    [StringLength(120)]
    public string? State { get; set; }

    [Required, StringLength(120)]
    public string Country { get; set; } = "United States";

    [Display(Name = "Arrive")]
    public DateOnly ArriveDate { get; set; }

    [Display(Name = "Depart")]
    public DateOnly DepartDate { get; set; }

    [Display(Name = "Transport")]
    public TransportMode TransportMode { get; set; } = TransportMode.Unspecified;

    [Display(Name = "Lodging"), StringLength(160)]
    public string? LodgingName { get; set; }

    [Display(Name = "Confirmation #"), StringLength(80)]
    public string? ConfirmationNumber { get; set; }

    [StringLength(1000)]
    public string? Notes { get; set; }

    // 1-based order of this leg within the trip.
    public int Sequence { get; set; }
}
