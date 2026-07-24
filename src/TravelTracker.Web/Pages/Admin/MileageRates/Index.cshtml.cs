using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TravelTracker.Web.Data;
using TravelTracker.Web.Data.Entities;

namespace TravelTracker.Web.Pages.Admin.MileageRates;

// Effective-dated rate table management. Under /Admin (RequireAdmin). Adding a
// later-dated row for the same (jurisdiction, vehicle, unit) supersedes older ones
// from its EffectiveDate forward — the resolver picks the latest ≤ the entry date.
// Deleting a rate never corrupts history: MileageEntry.RateId is SetNull, and each
// entry keeps its own frozen Rate/Amount.
public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    public IndexModel(AppDbContext db) => _db = db;

    public List<MileageRate> Rates { get; private set; } = new();

    [BindProperty] public InputModel Input { get; set; } = new();

    public class InputModel
    {
        [Required, DataType(DataType.Date), Display(Name = "Effective date")]
        public DateOnly EffectiveDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);

        [Required, StringLength(60)]
        public string Jurisdiction { get; set; } = "US";

        [Display(Name = "Vehicle type")]
        public VehicleType VehicleType { get; set; } = VehicleType.Car;

        public DistanceUnit Unit { get; set; } = DistanceUnit.Miles;

        [Required, Range(0, 1000), Display(Name = "Rate (per unit)")]
        public decimal Rate { get; set; }

        [StringLength(200)]
        public string? Notes { get; set; }
    }

    public async Task OnGetAsync() => await LoadAsync();

    private async Task LoadAsync() =>
        Rates = await _db.MileageRates
            .OrderBy(r => r.Jurisdiction).ThenBy(r => r.VehicleType).ThenBy(r => r.Unit)
            .ThenByDescending(r => r.EffectiveDate)
            .ToListAsync();

    public async Task<IActionResult> OnPostCreateAsync()
    {
        var jurisdiction = Input.Jurisdiction?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(jurisdiction))
            ModelState.AddModelError("Input.Jurisdiction", "Jurisdiction is required.");
        else if (await _db.MileageRates.AnyAsync(r =>
                     r.Jurisdiction == jurisdiction && r.VehicleType == Input.VehicleType &&
                     r.Unit == Input.Unit && r.EffectiveDate == Input.EffectiveDate))
            ModelState.AddModelError(string.Empty,
                "A rate for that jurisdiction / vehicle / unit already takes effect on that date.");

        if (!ModelState.IsValid)
        {
            await LoadAsync();
            return Page();
        }

        _db.MileageRates.Add(new MileageRate
        {
            EffectiveDate = Input.EffectiveDate,
            Jurisdiction = jurisdiction,
            VehicleType = Input.VehicleType,
            Unit = Input.Unit,
            Rate = Input.Rate,
            Notes = string.IsNullOrWhiteSpace(Input.Notes) ? null : Input.Notes.Trim(),
        });
        await _db.SaveChangesAsync();
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var rate = await _db.MileageRates.FindAsync(id);
        if (rate is not null)
        {
            // Existing entries pointing here have RateId set null by the FK; their
            // frozen Rate/Amount are unaffected.
            _db.MileageRates.Remove(rate);
            await _db.SaveChangesAsync();
        }
        return RedirectToPage();
    }
}
