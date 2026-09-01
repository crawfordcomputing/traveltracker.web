using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using TravelTracker.Web.Data;
using TravelTracker.Web.Data.Entities;
using TravelTracker.Web.Domain;

namespace TravelTracker.Web.Pages.Trips;

public class EditLegModel : TripPageModel
{
    public EditLegModel(AppDbContext db, TeamAccess team) : base(db, team) { }

    [BindProperty] public LegInput Input { get; set; } = new();
    public int TripId { get; private set; }

    private const string ItineraryLockedMessage =
        "This trip's itinerary is locked in its current status. Use \"Revise itinerary\" "
        + "to send an approved trip back to draft before changing its legs.";
    public string TripPurpose { get; private set; } = string.Empty;

    public SelectList Countries { get; private set; } = default!;
    public SelectList States { get; private set; } = default!;

    public class LegInput
    {
        public int Id { get; set; }
        [Required, StringLength(120)] public string City { get; set; } = string.Empty;
        [StringLength(120)] public string? State { get; set; }
        [Required, StringLength(120)] public string Country { get; set; } = "United States";

        [Display(Name = "Arrive"), DataType(DataType.Date)]
        public DateOnly ArriveDate { get; set; }

        [Display(Name = "Depart"), DataType(DataType.Date)]
        public DateOnly DepartDate { get; set; }

        [Display(Name = "Transport")] public TransportMode TransportMode { get; set; } = TransportMode.Unspecified;
        [Display(Name = "Lodging"), StringLength(160)] public string? LodgingName { get; set; }
        [Display(Name = "Confirmation #"), StringLength(80)] public string? ConfirmationNumber { get; set; }
        [StringLength(1000)] public string? Notes { get; set; }
    }

    private async Task LoadLookupsAsync()
    {
        Countries = new SelectList(
            await Db.Countries.OrderBy(c => c.Name).ToListAsync(), "Name", "Name", Input.Country);
        States = new SelectList(
            await Db.UsStates.OrderBy(s => s.Name).ToListAsync(), "Name", "Name", Input.State);
    }

    public async Task<IActionResult> OnGetAsync(int id, int legId)
    {
        var trip = await LoadAuthorizedTripAsync(id, q => q.Include(t => t.Destinations));
        if (trip is null) return NotFound();

        var leg = trip.Destinations.FirstOrDefault(d => d.Id == legId);
        if (leg is null) return NotFound();

        if (!TripStatusRules.ItineraryEditable(trip.Status))
        {
            TempData["TripError"] = ItineraryLockedMessage;
            return RedirectToPage("Details", new { id });
        }

        TripId = trip.Id;
        TripPurpose = trip.Purpose;
        Input = new LegInput
        {
            Id = leg.Id,
            City = leg.City,
            State = leg.State,
            Country = leg.Country,
            ArriveDate = leg.ArriveDate,
            DepartDate = leg.DepartDate,
            TransportMode = leg.TransportMode,
            LodgingName = leg.LodgingName,
            ConfirmationNumber = leg.ConfirmationNumber,
            Notes = leg.Notes
        };
        await LoadLookupsAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        var trip = await LoadAuthorizedTripAsync(id, q => q.Include(t => t.Destinations));
        if (trip is null) return NotFound();

        var leg = trip.Destinations.FirstOrDefault(d => d.Id == Input.Id);
        if (leg is null) return NotFound();

        if (!TripStatusRules.ItineraryEditable(trip.Status))
        {
            TempData["TripError"] = ItineraryLockedMessage;
            return RedirectToPage("Details", new { id });
        }

        TripId = trip.Id;
        TripPurpose = trip.Purpose;

        var legError = TripDateRules.ValidateLeg(Input.ArriveDate, Input.DepartDate);
        if (legError is not null)
            ModelState.AddModelError("Input.DepartDate", legError);

        if (!ModelState.IsValid)
        {
            await LoadLookupsAsync();
            return Page();
        }

        leg.City = Input.City.Trim();
        leg.State = string.IsNullOrWhiteSpace(Input.State) ? null : Input.State.Trim();
        leg.Country = Input.Country.Trim();
        leg.ArriveDate = Input.ArriveDate;
        leg.DepartDate = Input.DepartDate;
        leg.TransportMode = Input.TransportMode;
        leg.LodgingName = string.IsNullOrWhiteSpace(Input.LodgingName) ? null : Input.LodgingName.Trim();
        leg.ConfirmationNumber = string.IsNullOrWhiteSpace(Input.ConfirmationNumber) ? null : Input.ConfirmationNumber.Trim();
        leg.Notes = string.IsNullOrWhiteSpace(Input.Notes) ? null : Input.Notes.Trim();

        // Keep the trip window covering all legs after the edit.
        var (start, end) = TripDateRules.ReconcileWindow(trip.StartDate, trip.EndDate, trip.Destinations);
        trip.StartDate = start;
        trip.EndDate = end;

        await Db.SaveChangesAsync();
        return RedirectToPage("Details", new { id });
    }
}
