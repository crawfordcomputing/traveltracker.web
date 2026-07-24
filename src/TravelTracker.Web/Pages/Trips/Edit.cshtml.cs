using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using TravelTracker.Web.Data;
using TravelTracker.Web.Data.Entities;
using TravelTracker.Web.Domain;

namespace TravelTracker.Web.Pages.Trips;

public class EditModel : TripPageModel
{
    public EditModel(AppDbContext db, TeamAccess team) : base(db, team) { }

    [BindProperty] public InputModel Input { get; set; } = new();
    public string TravelerName { get; private set; } = string.Empty;
    public SelectList CostCenters { get; private set; } = default!;
    public SelectList ProjectCodes { get; private set; } = default!;

    private async Task LoadListsAsync()
    {
        CostCenters = await Db.CostCenterSelectListAsync(Input.CostCenterId);
        ProjectCodes = await Db.ProjectCodeSelectListAsync(Input.ProjectCodeId);
    }

    public class InputModel
    {
        public int Id { get; set; }

        [Required, StringLength(200)]
        public string Purpose { get; set; } = string.Empty;

        [Required, Display(Name = "Start date"), DataType(DataType.Date)]
        public DateOnly StartDate { get; set; }

        [Required, Display(Name = "End date"), DataType(DataType.Date)]
        public DateOnly EndDate { get; set; }

        [Display(Name = "Trip type")]
        public TripType Type { get; set; } = TripType.Unspecified;

        [Display(Name = "Project code")]
        public int? ProjectCodeId { get; set; }

        [Display(Name = "Cost center")]
        public int? CostCenterId { get; set; }

        [StringLength(1000)]
        public string? Notes { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var trip = await LoadAuthorizedTripAsync(id, q => q.Include(t => t.Traveler));
        if (trip is null) return NotFound();

        TravelerName = trip.Traveler?.DisplayName ?? string.Empty;
        Input = new InputModel
        {
            Id = trip.Id,
            Purpose = trip.Purpose,
            StartDate = trip.StartDate,
            EndDate = trip.EndDate,
            Type = trip.Type,
            ProjectCodeId = trip.ProjectCodeId,
            CostCenterId = trip.CostCenterId,
            Notes = trip.Notes
        };
        await LoadListsAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var trip = await LoadAuthorizedTripAsync(Input.Id, q => q.Include(t => t.Traveler));
        if (trip is null) return NotFound();

        TravelerName = trip.Traveler?.DisplayName ?? string.Empty;

        if (!TripDateRules.IsValidTripRange(Input.StartDate, Input.EndDate))
            ModelState.AddModelError("Input.EndDate", "End date cannot be before the start date.");

        if (Input.CostCenterId is int ccId && !await Db.CostCenters.AnyAsync(c => c.Id == ccId))
            ModelState.AddModelError("Input.CostCenterId", "Unknown cost center.");
        if (Input.ProjectCodeId is int pcId && !await Db.ProjectCodes.AnyAsync(p => p.Id == pcId))
            ModelState.AddModelError("Input.ProjectCodeId", "Unknown project code.");

        if (!ModelState.IsValid)
        {
            await LoadListsAsync();
            return Page();
        }

        trip.Purpose = Input.Purpose.Trim();
        trip.StartDate = Input.StartDate;
        trip.EndDate = Input.EndDate;
        trip.Type = Input.Type;
        trip.ProjectCodeId = Input.ProjectCodeId;
        trip.CostCenterId = Input.CostCenterId;
        trip.Notes = string.IsNullOrWhiteSpace(Input.Notes) ? null : Input.Notes.Trim();
        await Db.SaveChangesAsync();

        return RedirectToPage("Details", new { id = trip.Id });
    }
}
