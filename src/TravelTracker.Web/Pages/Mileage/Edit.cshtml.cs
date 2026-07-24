using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using TravelTracker.Web.Data;
using TravelTracker.Web.Data.Entities;
using TravelTracker.Web.Domain;

namespace TravelTracker.Web.Pages.Mileage;

public class EditModel : MileagePageModel
{
    public EditModel(AppDbContext db, TeamAccess team) : base(db, team) { }

    public MileageEntry Entry { get; private set; } = default!;

    [BindProperty] public CreateModel.InputModel Input { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var entry = await LoadAuthorizedEntryAsync(id);
        if (entry is null) return NotFound();
        Entry = entry;

        Input = new CreateModel.InputModel
        {
            Date = entry.Date,
            Distance = entry.Distance,
            Unit = entry.Unit,
            IsRoundTrip = entry.IsRoundTrip,
            CommuteDeduction = entry.CommuteDeduction,
            Jurisdiction = entry.Jurisdiction,
            VehicleType = entry.VehicleType,
            Purpose = entry.Purpose,
            Waypoints = WaypointText.Format(entry.Waypoints),
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        var entry = await LoadAuthorizedEntryAsync(id);
        if (entry is null) return NotFound();
        Entry = entry;

        if (!ModelState.IsValid)
            return Page();

        // Re-freeze against the (possibly changed) date/vehicle/jurisdiction. This is
        // still "freeze at entry" — the entry is being re-entered. Editing the RATE
        // TABLE never reaches here, so existing untouched entries stay frozen.
        var rate = await ResolveRateAsync(Input.Jurisdiction.Trim(), Input.VehicleType, Input.Unit, Input.Date);
        if (rate is null)
        {
            ModelState.AddModelError(string.Empty,
                $"No mileage rate is configured for {Input.Jurisdiction.Trim()} / {Input.VehicleType} / {Input.Unit} " +
                $"effective on {Input.Date:MMM d, yyyy}. Ask an admin to add one under Admin → Mileage rates.");
            return Page();
        }

        var billable = MileageMath.BillableDistance(Input.Distance, Input.IsRoundTrip, Input.CommuteDeduction);

        entry.Date = Input.Date;
        entry.Distance = Input.Distance;
        entry.Unit = Input.Unit;
        entry.IsRoundTrip = Input.IsRoundTrip;
        entry.CommuteDeduction = Input.CommuteDeduction;
        entry.Jurisdiction = Input.Jurisdiction.Trim();
        entry.VehicleType = Input.VehicleType;
        entry.RateId = rate.Id;
        entry.Rate = rate.Rate;
        entry.Amount = MileageMath.Amount(billable, rate.Rate);
        entry.Purpose = string.IsNullOrWhiteSpace(Input.Purpose) ? null : Input.Purpose.Trim();

        // Replace waypoints wholesale — simplest correct behaviour for a re-entry.
        Db.MileageWaypoints.RemoveRange(entry.Waypoints);
        entry.Waypoints = WaypointText.Parse(Input.Waypoints);

        await Db.SaveChangesAsync();
        return RedirectToPage("/Trips/Details", new { id = entry.TripId });
    }
}
