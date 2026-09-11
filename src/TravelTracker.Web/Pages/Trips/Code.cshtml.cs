using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TravelTracker.Web.Data;
using TravelTracker.Web.Domain;

namespace TravelTracker.Web.Pages.Trips;

// GET /Trips/Code/{code}: resolve a trip reference code to its Details page.
// Input is normalized (case, spaces, hyphens, O/0 and I/L/1 aliases), so a code
// read off a Concur report or typed by hand still resolves. Unknown, malformed,
// and not-visible codes all return the same 404 so the route can't be used to
// probe which codes exist (ADR-0004). Access goes through TripAccess like every
// other Trips page; the code is an identifier, never a capability.
public class CodeModel : TripPageModel
{
    public CodeModel(AppDbContext db, TeamAccess team) : base(db, team) { }

    public async Task<IActionResult> OnGetAsync(string code)
    {
        if (!TripCode.TryParse(code, out var canonical)) return NotFound();

        var id = await Db.Trips
            .Where(t => t.Code == canonical)
            .Select(t => (int?)t.Id)
            .FirstOrDefaultAsync();
        if (id is null) return NotFound();

        var trip = await LoadAuthorizedTripAsync(id.Value);
        if (trip is null) return NotFound();

        return RedirectToPage("Details", new { id = trip.Id });
    }
}
