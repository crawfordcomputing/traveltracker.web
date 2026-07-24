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

public class CreateModel : TripPageModel
{
    private readonly UserManager<AppUser> _users;

    public CreateModel(AppDbContext db, UserManager<AppUser> users, TeamAccess team)
        : base(db, team)
    {
        _users = users;
    }

    [BindProperty] public InputModel Input { get; set; } = new();
    // True when the user may create a trip for someone other than themselves (shows the
    // traveler picker): Admin, a Manager with department teammates, or an Arranger with
    // at least one assignment.
    public bool ShowTraveler { get; private set; }
    public SelectList Travelers { get; private set; } = default!;
    public SelectList CostCenters { get; private set; } = default!;
    public SelectList ProjectCodes { get; private set; } = default!;

    // The traveler ids this user may create for; null means "anyone" (Admin).
    private IReadOnlySet<string>? _allowedTravelerIds;

    public class InputModel
    {
        [Required, StringLength(200)]
        public string Purpose { get; set; } = string.Empty;

        [Required, Display(Name = "Start date"), DataType(DataType.Date)]
        public DateOnly StartDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);

        [Required, Display(Name = "End date"), DataType(DataType.Date)]
        public DateOnly EndDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);

        [Display(Name = "Trip type")]
        public TripType Type { get; set; } = TripType.Unspecified;

        [Display(Name = "Project code")]
        public int? ProjectCodeId { get; set; }

        [Display(Name = "Cost center")]
        public int? CostCenterId { get; set; }

        [StringLength(1000)]
        public string? Notes { get; set; }

        // Only honoured for Manager/Admin; employees always create for themselves.
        [Display(Name = "Traveler")]
        public string? TravelerId { get; set; }
    }

    private async Task LoadAsync()
    {
        var reachable = await Team.ReachableTravelerIdsAsync(User);
        var scope = TripAccess.Scope(User, reachable);

        // Admin picks from all users; a Manager picks self + their department, an
        // Arranger self + assigned travelers. A plain Employee gets no picker
        // (always creates for themselves).
        _allowedTravelerIds = scope.All ? null : scope.TravelerIds;
        ShowTraveler = scope.All || reachable.Count > 0;

        if (ShowTraveler)
        {
            var travelers = scope.All
                ? await Db.Users.OrderBy(u => u.DisplayName).ToListAsync()
                : await Db.Users.Where(u => scope.TravelerIds.Contains(u.Id))
                    .OrderBy(u => u.DisplayName).ToListAsync();
            Travelers = new SelectList(travelers, "Id", "DisplayName", Input.TravelerId);
        }

        CostCenters = await Db.CostCenterSelectListAsync(Input.CostCenterId);
        ProjectCodes = await Db.ProjectCodeSelectListAsync(Input.ProjectCodeId);
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var selfId = _users.GetUserId(User);
        Input.TravelerId = selfId;
        // Prefill the traveler's default cost center so common trips don't retype it.
        Input.CostCenterId = await Db.Users
            .Where(u => u.Id == selfId)
            .Select(u => u.DefaultCostCenterId)
            .FirstOrDefaultAsync();
        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadAsync();

        if (!TripDateRules.IsValidTripRange(Input.StartDate, Input.EndDate))
            ModelState.AddModelError("Input.EndDate", "End date can't be before the start date.");

        // Users without a picker always create for themselves; those with one may pick
        // another traveler only from the set they're allowed to act for.
        var selfId = _users.GetUserId(User)!;
        var travelerId = ShowTraveler && !string.IsNullOrEmpty(Input.TravelerId)
            ? Input.TravelerId!
            : selfId;

        // _allowedTravelerIds == null means Admin (anyone); otherwise the id must be
        // self or a reachable traveler (department teammate / assigned traveler).
        var travelerAllowed = travelerId == selfId
            || _allowedTravelerIds is null
            || _allowedTravelerIds.Contains(travelerId);
        if (!travelerAllowed)
            ModelState.AddModelError("Input.TravelerId", "Unknown traveler.");
        else if (await Db.Users.FindAsync(travelerId) is null)
            ModelState.AddModelError("Input.TravelerId", "Unknown traveler.");

        if (Input.CostCenterId is int ccId && !await Db.CostCenters.AnyAsync(c => c.Id == ccId))
            ModelState.AddModelError("Input.CostCenterId", "Unknown cost center.");
        if (Input.ProjectCodeId is int pcId && !await Db.ProjectCodes.AnyAsync(p => p.Id == pcId))
            ModelState.AddModelError("Input.ProjectCodeId", "Unknown project code.");

        if (!ModelState.IsValid)
            return Page();

        var trip = new Trip
        {
            TravelerId = travelerId,
            Purpose = Input.Purpose.Trim(),
            Status = TripStatus.Draft,
            StartDate = Input.StartDate,
            EndDate = Input.EndDate,
            Type = Input.Type,
            ProjectCodeId = Input.ProjectCodeId,
            CostCenterId = Input.CostCenterId,
            Notes = string.IsNullOrWhiteSpace(Input.Notes) ? null : Input.Notes.Trim(),
            CreatedById = selfId,
            CreatedAt = DateTimeOffset.UtcNow
        };
        Db.Trips.Add(trip);
        await Db.SaveChangesAsync();

        return RedirectToPage("Details", new { id = trip.Id });
    }
}
