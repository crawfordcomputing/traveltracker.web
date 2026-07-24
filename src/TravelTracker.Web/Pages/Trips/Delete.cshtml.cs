using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TravelTracker.Web.Data;
using TravelTracker.Web.Data.Entities;
using TravelTracker.Web.Domain;

namespace TravelTracker.Web.Pages.Trips;

public class DeleteModel : TripPageModel
{
    public DeleteModel(AppDbContext db, TeamAccess team) : base(db, team) { }

    public Trip Trip { get; private set; } = default!;

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var trip = await LoadAuthorizedTripAsync(id,
            q => q.Include(t => t.Traveler).Include(t => t.Destinations));
        if (trip is null) return NotFound();

        Trip = trip;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        var trip = await LoadAuthorizedTripAsync(id, q => q.Include(t => t.Destinations));
        if (trip is null) return NotFound();

        Db.Trips.Remove(trip);
        await Db.SaveChangesAsync();
        return RedirectToPage("Index");
    }
}
