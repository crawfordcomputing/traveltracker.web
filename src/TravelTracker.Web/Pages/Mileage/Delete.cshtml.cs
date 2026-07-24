using Microsoft.AspNetCore.Mvc;
using TravelTracker.Web.Data;
using TravelTracker.Web.Data.Entities;
using TravelTracker.Web.Domain;

namespace TravelTracker.Web.Pages.Mileage;

public class DeleteModel : MileagePageModel
{
    public DeleteModel(AppDbContext db, TeamAccess team) : base(db, team) { }

    public MileageEntry Entry { get; private set; } = default!;

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var entry = await LoadAuthorizedEntryAsync(id);
        if (entry is null) return NotFound();
        Entry = entry;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        var entry = await LoadAuthorizedEntryAsync(id);
        if (entry is null) return NotFound();

        var tripId = entry.TripId;
        // Waypoints cascade with the entry (owned).
        Db.MileageEntries.Remove(entry);
        await Db.SaveChangesAsync();
        return RedirectToPage("/Trips/Details", new { id = tripId });
    }
}
