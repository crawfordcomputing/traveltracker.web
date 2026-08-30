using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using TravelTracker.Web.Data;
using TravelTracker.Web.Data.Entities;
using TravelTracker.Web.Domain;

namespace TravelTracker.Web.Pages.Mileage;

public class CreateModel : MileagePageModel
{
    public CreateModel(AppDbContext db, TeamAccess team) : base(db, team) { }

    public Trip Trip { get; private set; } = default!;

    // Advisory soft-warning shown when mileage is added to a not-yet-approved trip
    // (null once Approved/Completed). Never blocks saving. See ADR-0002.
    public string? CostWarning => TripStatusRules.CostEntryWarning(Trip.Status);

    [BindProperty] public InputModel Input { get; set; } = new();

    // Live preview of the frozen amount is done server-side after post; nothing here
    // recomputes client-side (no JS build step), matching the app's no-JS convention.
    public class InputModel
    {
        [Required, DataType(DataType.Date)]
        public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.Today);

        [Required, Range(0.01, 100_000, ErrorMessage = "Enter a distance greater than zero.")]
        public decimal Distance { get; set; }

        public DistanceUnit Unit { get; set; } = DistanceUnit.Miles;

        [Display(Name = "Round trip")]
        public bool IsRoundTrip { get; set; }

        [Range(0, 100_000, ErrorMessage = "Commute deduction cannot be negative.")]
        [Display(Name = "Commute deduction")]
        public decimal CommuteDeduction { get; set; }

        [Required, StringLength(60)]
        public string Jurisdiction { get; set; } = "US";

        [Display(Name = "Vehicle type")]
        public VehicleType VehicleType { get; set; } = VehicleType.Car;

        [StringLength(300)]
        public string? Purpose { get; set; }

        [Display(Name = "Waypoints (one stop per line)")]
        public string? Waypoints { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(int tripId)
    {
        var trip = await LoadAuthorizedTripAsync(tripId);
        if (trip is null) return NotFound();
        Trip = trip;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int tripId)
    {
        var trip = await LoadAuthorizedTripAsync(tripId);
        if (trip is null) return NotFound();
        Trip = trip;

        if (!ModelState.IsValid)
            return Page();

        // Resolve the rate IN EFFECT on the mileage date and freeze it. No rate →
        // block with a clear pointer to the rate table rather than saving a 0 amount.
        var rate = await ResolveRateAsync(Input.Jurisdiction.Trim(), Input.VehicleType, Input.Unit, Input.Date);
        if (rate is null)
        {
            ModelState.AddModelError(string.Empty,
                $"No mileage rate is configured for {Input.Jurisdiction.Trim()} / {Input.VehicleType} / {Input.Unit} " +
                $"effective on {Input.Date:MMM d, yyyy}. Ask an admin to add one under Admin → Mileage rates.");
            return Page();
        }

        var billable = MileageMath.BillableDistance(Input.Distance, Input.IsRoundTrip, Input.CommuteDeduction);

        var entry = new MileageEntry
        {
            TripId = trip.Id,
            Date = Input.Date,
            Distance = Input.Distance,
            Unit = Input.Unit,
            IsRoundTrip = Input.IsRoundTrip,
            CommuteDeduction = Input.CommuteDeduction,
            Jurisdiction = Input.Jurisdiction.Trim(),
            VehicleType = Input.VehicleType,
            RateId = rate.Id,
            Rate = rate.Rate,                              // frozen
            Amount = MileageMath.Amount(billable, rate.Rate), // frozen
            Purpose = string.IsNullOrWhiteSpace(Input.Purpose) ? null : Input.Purpose.Trim(),
            CreatedById = CurrentUserId,
            CreatedAt = DateTimeOffset.UtcNow,
            Waypoints = WaypointText.Parse(Input.Waypoints),
        };
        Db.MileageEntries.Add(entry);
        await Db.SaveChangesAsync();

        return RedirectToPage("/Trips/Details", new { id = trip.Id });
    }
}
