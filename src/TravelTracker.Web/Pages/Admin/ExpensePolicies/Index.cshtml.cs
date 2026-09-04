using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using TravelTracker.Web.Data;
using TravelTracker.Web.Data.Entities;

namespace TravelTracker.Web.Pages.Admin.ExpensePolicies;

// Configurable per-category expense guidelines. Under /Admin (RequireAdmin). One
// row per category; a category with no row is uncapped. Caps are advisory only —
// they drive the "Over guideline" badge and never block entry. See ADR-0003.
public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    public IndexModel(AppDbContext db) => _db = db;

    public List<ExpensePolicy> Policies { get; private set; } = new();

    // Categories that don't yet have a cap — the only ones "Add" offers, so the
    // unique-per-category rule can't be violated from the happy path.
    public SelectList UncappedCategories { get; private set; } = default!;
    public bool HasUncapped { get; private set; }

    [BindProperty] public InputModel Input { get; set; } = new();

    public class InputModel
    {
        [Required]
        public ExpenseCategory Category { get; set; }

        [Required, Range(0, 1_000_000), Display(Name = "Cap amount")]
        public decimal CapAmount { get; set; }

        [StringLength(200)]
        public string? Notes { get; set; }
    }

    public async Task OnGetAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        Policies = await _db.ExpensePolicies
            .OrderBy(p => p.Category)
            .ToListAsync();

        var capped = Policies.Select(p => p.Category).ToHashSet();
        var open = Enum.GetValues<ExpenseCategory>()
            .Where(c => c != ExpenseCategory.Unspecified && !capped.Contains(c))
            .ToList();
        HasUncapped = open.Count > 0;
        UncappedCategories = new SelectList(open);
    }

    public async Task<IActionResult> OnPostCreateAsync()
    {
        if (Input.Category == ExpenseCategory.Unspecified)
            ModelState.AddModelError("Input.Category", "Choose a category.");
        else if (await _db.ExpensePolicies.AnyAsync(p => p.Category == Input.Category))
            ModelState.AddModelError(string.Empty, "That category already has a guideline. Edit it instead.");

        if (!ModelState.IsValid)
        {
            await LoadAsync();
            return Page();
        }

        _db.ExpensePolicies.Add(new ExpensePolicy
        {
            Category = Input.Category,
            CapAmount = Input.CapAmount,
            Notes = string.IsNullOrWhiteSpace(Input.Notes) ? null : Input.Notes.Trim(),
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();
        return RedirectToPage();
    }

    // Inline per-row edit of the amount (and note). Category is fixed once set.
    // capAmount is nullable on purpose: a blank or non-numeric field must be rejected,
    // not bound to 0 (which would flag every expense in the category).
    public async Task<IActionResult> OnPostUpdateAsync(int id, decimal? capAmount, string? notes)
    {
        var policy = await _db.ExpensePolicies.FindAsync(id);
        if (policy is null) return RedirectToPage();

        if (capAmount is null || capAmount < 0 || capAmount > 1_000_000)
        {
            ModelState.AddModelError(string.Empty, "Cap amount must be a number between 0 and 1,000,000.");
            await LoadAsync();
            return Page();
        }

        policy.CapAmount = capAmount.Value;
        policy.Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        policy.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync();
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var policy = await _db.ExpensePolicies.FindAsync(id);
        if (policy is not null)
        {
            // Deleting a guideline just makes that category uncapped again. Past
            // expenses are unaffected (the badge is computed live, nothing frozen).
            _db.ExpensePolicies.Remove(policy);
            await _db.SaveChangesAsync();
        }
        return RedirectToPage();
    }
}
