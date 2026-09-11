using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using TravelTracker.Web.Data;
using TravelTracker.Web.Data.Entities;
using TravelTracker.Web.Domain;

namespace TravelTracker.Web.Pages.Trips;

public class IndexModel : TripPageModel
{
    private readonly UserManager<AppUser> _users;

    public IndexModel(AppDbContext db, UserManager<AppUser> users, TeamAccess team)
        : base(db, team)
    {
        _users = users;
    }

    public const int PageSize = 20;

    public IList<Trip> Trips { get; private set; } = new List<Trip>();
    // Manager/Admin — gates the duty-of-care "Who's out" roster (managers see it
    // scoped to their department, admins org-wide).
    public bool CanSeeTeamRoster { get; private set; }
    // True when the user can see travelers other than themselves (Manager/Admin, or an
    // Arranger with at least one assignment) — drives the Traveler column + filter.
    public bool ShowTraveler { get; private set; }
    public SelectList? TravelerFilter { get; private set; }

    public int TotalCount { get; private set; }
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);

    [BindProperty(SupportsGet = true)] public string? TravelerId { get; set; }
    [BindProperty(SupportsGet = true)] public TripStatus? Status { get; set; }
    [BindProperty(SupportsGet = true), DataType(DataType.Date)] public DateOnly? From { get; set; }
    [BindProperty(SupportsGet = true), DataType(DataType.Date)] public DateOnly? To { get; set; }
    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    // "upcoming" | "past" | "all" (default upcoming).
    [BindProperty(SupportsGet = true)] public string When { get; set; } = "upcoming";
    [BindProperty(SupportsGet = true)] public int PageNumber { get; set; } = 1;

    public async Task OnGetAsync()
    {
        var reachable = await Team.ReachableTravelerIdsAsync(User);
        var scope = TripAccess.Scope(User, reachable);
        CanSeeTeamRoster = TripAccess.CanSeeTeamRoster(User);
        ShowTraveler = scope.All || reachable.Count > 0;

        var query = Db.Trips
            .Include(t => t.Traveler)
            .Include(t => t.Destinations)
            .AsQueryable();

        // Restrict to the travelers this user may see, unless they have global reach.
        if (!scope.All)
            query = query.Where(t => scope.TravelerIds.Contains(t.TravelerId));

        // A traveler filter narrows within the visible set (ignored if it names a
        // traveler outside the scope, which would just be empty anyway).
        if (!string.IsNullOrEmpty(TravelerId) && (scope.All || scope.TravelerIds.Contains(TravelerId)))
            query = query.Where(t => t.TravelerId == TravelerId);

        if (ShowTraveler)
        {
            var travelers = scope.All
                ? await Db.Users.OrderBy(u => u.DisplayName).ToListAsync()
                : await Db.Users.Where(u => scope.TravelerIds.Contains(u.Id))
                    .OrderBy(u => u.DisplayName).ToListAsync();
            TravelerFilter = new SelectList(travelers, "Id", "DisplayName", TravelerId);
        }

        if (Status is not null)
            query = query.Where(t => t.Status == Status);

        if (From is not null)
            query = query.Where(t => t.EndDate >= From);

        if (To is not null)
            query = query.Where(t => t.StartDate <= To);

        if (!string.IsNullOrWhiteSpace(Search))
        {
            var term = Search.Trim();
            // A term that parses as a full trip code (any casing/spacing) or as just
            // its 6-character random part also matches on Code (ADR-0004). The
            // suffix is only unique per year, so it's a trailing match, not equality.
            // Both values contain only [0-9A-Z-], so they carry no LIKE wildcards.
            var fullCode = TripCode.TryParse(term, out var parsed) ? parsed : null;
            var suffixLike = TripCode.TryParseSuffix(term, out var sfx) ? $"%-{sfx}" : null;
            query = query.Where(t =>
                EF.Functions.Like(t.Purpose, $"%{term}%") ||
                t.Destinations.Any(d => EF.Functions.Like(d.City, $"%{term}%")) ||
                (fullCode != null && t.Code == fullCode) ||
                (suffixLike != null && EF.Functions.Like(t.Code, suffixLike)));
        }

        var today = DateOnly.FromDateTime(DateTime.Today);
        query = When switch
        {
            "past" => query.Where(t => t.EndDate < today).OrderByDescending(t => t.StartDate),
            "all" => query.OrderByDescending(t => t.StartDate),
            _ => query.Where(t => t.EndDate >= today).OrderBy(t => t.StartDate),
        };

        TotalCount = await query.CountAsync();
        if (PageNumber < 1) PageNumber = 1;

        Trips = await query
            .Skip((PageNumber - 1) * PageSize)
            .Take(PageSize)
            .ToListAsync();
    }

    // Clone a trip (and its legs) into a new Draft owned by the same traveler.
    // Handy for recurring routes; the copy is fully editable before use.
    public async Task<IActionResult> OnPostCloneAsync(int id)
    {
        var source = await LoadAuthorizedTripAsync(id, q => q.Include(t => t.Destinations));
        if (source is null) return NotFound();

        var clone = new Trip
        {
            TravelerId = source.TravelerId,
            Purpose = $"{source.Purpose} (copy)",
            Status = TripStatus.Draft,
            Type = source.Type,
            ProjectCodeId = source.ProjectCodeId,
            CostCenterId = source.CostCenterId,
            StartDate = source.StartDate,
            EndDate = source.EndDate,
            Notes = source.Notes,
            CreatedById = _users.GetUserId(User),
            CreatedAt = DateTimeOffset.UtcNow,
            Destinations = source.Destinations
                .OrderBy(d => d.Sequence)
                .Select(d => new Destination
                {
                    City = d.City,
                    State = d.State,
                    Country = d.Country,
                    ArriveDate = d.ArriveDate,
                    DepartDate = d.DepartDate,
                    TransportMode = d.TransportMode,
                    LodgingName = d.LodgingName,
                    ConfirmationNumber = d.ConfirmationNumber,
                    Notes = d.Notes,
                    Sequence = d.Sequence
                }).ToList()
        };

        Db.Trips.Add(clone);
        await Db.SaveChangesAsync();
        return RedirectToPage("Details", new { id = clone.Id });
    }
}
