using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TravelTracker.Web.Data;
using TravelTracker.Web.Data.Entities;
using TravelTracker.Web.Domain;
using TravelTracker.Web.Services;

namespace TravelTracker.Web.Pages.Expenses;

public class CreateModel : ExpensePageModel
{
    private readonly IReceiptStorage _storage;
    private readonly IConfiguration _config;

    public CreateModel(AppDbContext db, TeamAccess team, IReceiptStorage storage, IConfiguration config)
        : base(db, team)
    {
        _storage = storage;
        _config = config;
    }

    public Trip Trip { get; private set; } = default!;

    // Advisory soft-warning shown when costs are added to a not-yet-approved trip
    // (null once Approved/Completed). Never blocks saving. See ADR-0002.
    public string? CostWarning => TripStatusRules.CostEntryWarning(Trip.Status);

    [BindProperty] public InputModel Input { get; set; } = new();
    [BindProperty] public IFormFile? Receipt { get; set; }
    // Set true when the user chooses to save past a duplicate warning (advisory,
    // confirm-once — never a hard policy block).
    [BindProperty] public bool ConfirmDuplicate { get; set; }

    // Populated on a re-render when the entered line looks like an existing one.
    public IReadOnlyList<Expense> DuplicateMatches { get; private set; } = Array.Empty<Expense>();
    // Advisory affidavit hint state for the view.
    public ReceiptRequirement ReceiptState { get; private set; } = ReceiptRequirement.NotRequired;
    // True when the form was prefilled from a saved autosave draft.
    public bool DraftRestored { get; private set; }

    // M3a is single-currency; the entry UI captures the base currency only. The
    // frozen FX triplet is written server-side (FxRate=1, BaseAmount=Amount).
    public string BaseCurrency => _config["Expenses:BaseCurrency"] ?? "USD";
    private int MaxReceiptMb => _config.GetValue("Storage:MaxReceiptMb", 10);
    public bool ReceiptRequired => _config.GetValue("Expenses:ReceiptRequired", false);
    public decimal ReceiptThreshold => _config.GetValue("Expenses:ReceiptThreshold", 25m);

    public class InputModel
    {
        public ExpenseCategory Category { get; set; } = ExpenseCategory.Unspecified;

        [Required, DataType(DataType.Date)]
        public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.Today);

        [StringLength(160)]
        public string? Vendor { get; set; }

        [StringLength(500)]
        public string? Description { get; set; }

        [Required, Range(0.01, 1_000_000, ErrorMessage = "Enter an amount greater than zero.")]
        public decimal Amount { get; set; }

        [Display(Name = "Personal / non-reimbursable")]
        public bool IsPersonal { get; set; }

        // Meals & Entertainment substantiation (soft-prompted, not required here).
        [StringLength(500)]
        public string? Attendees { get; set; }

        [StringLength(500), Display(Name = "Business purpose")]
        public string? BusinessPurpose { get; set; }

        // Typed justification when the receipt-required policy is on and no receipt
        // is attached. Captured but not hard-required (blocking deferred to M3d).
        [StringLength(500), Display(Name = "Missing-receipt affidavit")]
        public string? MissingReceiptAffidavit { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(int tripId)
    {
        var trip = await LoadAuthorizedTripAsync(tripId);
        if (trip is null) return NotFound();
        Trip = trip;

        // Restore an in-progress autosave draft, if one exists for this user+trip.
        var draft = await Db.ExpenseDrafts
            .FirstOrDefaultAsync(d => d.UserId == CurrentUserId && d.TripId == trip.Id);
        if (draft is not null)
        {
            Input = new InputModel
            {
                Category = draft.Category,
                Date = draft.Date ?? DateOnly.FromDateTime(DateTime.Today),
                Vendor = draft.Vendor,
                Description = draft.Description,
                Amount = draft.Amount ?? 0m,
                IsPersonal = draft.IsPersonal,
                Attendees = draft.Attendees,
                BusinessPurpose = draft.BusinessPurpose
            };
            DraftRestored = true;
        }
        return Page();
    }

    // Progressive-enhancement autosave: a small script posts the form here as the
    // user types, upserting one draft per user+trip. No ModelState gate — partial
    // input is expected. Returns JSON the script ignores unless it wants a timestamp.
    public async Task<IActionResult> OnPostAutosaveAsync(int tripId)
    {
        // Capture into a local so nullable flow-analysis narrows it for the assignment.
        var userId = CurrentUserId;
        var trip = await LoadAuthorizedTripAsync(tripId);
        if (trip is null || userId is null) return new JsonResult(new { ok = false });

        var draft = await Db.ExpenseDrafts
            .FirstOrDefaultAsync(d => d.UserId == userId && d.TripId == trip.Id);
        if (draft is null)
        {
            draft = new ExpenseDraft { UserId = userId, TripId = trip.Id };
            Db.ExpenseDrafts.Add(draft);
        }

        draft.Category = Input.Category;
        draft.Date = Input.Date;
        draft.Vendor = Trimmed(Input.Vendor);
        draft.Description = Trimmed(Input.Description);
        draft.Amount = Input.Amount <= 0m ? null : Input.Amount;
        draft.IsPersonal = Input.IsPersonal;
        draft.Attendees = Trimmed(Input.Attendees);
        draft.BusinessPurpose = Trimmed(Input.BusinessPurpose);
        draft.UpdatedAt = DateTimeOffset.UtcNow;

        await Db.SaveChangesAsync();
        return new JsonResult(new { ok = true, savedAt = draft.UpdatedAt });
    }

    // Discard the saved draft and reload a blank form.
    public async Task<IActionResult> OnPostDiscardDraftAsync(int tripId)
    {
        var trip = await LoadAuthorizedTripAsync(tripId);
        if (trip is null) return NotFound();

        var draft = await Db.ExpenseDrafts
            .FirstOrDefaultAsync(d => d.UserId == CurrentUserId && d.TripId == trip.Id);
        if (draft is not null)
        {
            Db.ExpenseDrafts.Remove(draft);
            await Db.SaveChangesAsync();
        }
        return RedirectToPage("Create", new { tripId });
    }

    public async Task<IActionResult> OnPostAsync(int tripId)
    {
        var trip = await LoadAuthorizedTripAsync(tripId);
        if (trip is null) return NotFound();
        Trip = trip;

        if (Receipt is not null)
        {
            var error = ReceiptUpload.Validate(
                Receipt.FileName, Receipt.ContentType, Receipt.Length, MaxReceiptMb);
            if (error is not null)
                ModelState.AddModelError(nameof(Receipt), error);
        }

        if (!ModelState.IsValid)
            return Page();

        // Advisory duplicate check (amount + date + vendor). Confirm-once: the first
        // time a match is seen we re-render with a banner; a second submit saves.
        var candidate = new Expense { Amount = Input.Amount, Date = Input.Date, Vendor = Input.Vendor };
        var existing = await Db.Expenses
            .Where(e => e.TripId == trip.Id)
            .ToListAsync();
        var matches = DuplicateExpenseCheck.FindMatches(candidate, existing);
        if (matches.Count > 0 && !ConfirmDuplicate)
        {
            DuplicateMatches = matches;
            ReceiptState = ReceiptAffidavitPolicy.Decision(
                Receipt is not null, Input.Amount, ReceiptRequired, ReceiptThreshold);
            return Page();
        }

        string? receiptKey = null;
        if (Receipt is not null)
        {
            await using var stream = Receipt.OpenReadStream();
            receiptKey = await _storage.SaveAsync(stream, Receipt.FileName, Receipt.ContentType);
        }

        // Freeze the money values at entry. Single-currency here: FxRate=1,
        // BaseAmount=Amount, no rate date.
        var expense = new Expense
        {
            TripId = trip.Id,
            Category = Input.Category,
            Date = Input.Date,
            Vendor = Trimmed(Input.Vendor),
            Description = Trimmed(Input.Description),
            Amount = Input.Amount,
            Currency = BaseCurrency,
            FxRate = 1m,
            FxRateDate = null,
            BaseAmount = Input.Amount,
            IsPersonal = Input.IsPersonal,
            Attendees = Trimmed(Input.Attendees),
            BusinessPurpose = Trimmed(Input.BusinessPurpose),
            MissingReceiptAffidavit = receiptKey is null ? Trimmed(Input.MissingReceiptAffidavit) : null,
            ReceiptPath = receiptKey,
            CreatedById = CurrentUserId,
            CreatedAt = DateTimeOffset.UtcNow
        };
        Db.Expenses.Add(expense);

        // Clear any autosave draft for this user+trip now that a real line exists.
        var draft = await Db.ExpenseDrafts
            .FirstOrDefaultAsync(d => d.UserId == CurrentUserId && d.TripId == trip.Id);
        if (draft is not null) Db.ExpenseDrafts.Remove(draft);

        await Db.SaveChangesAsync();

        return RedirectToPage("/Trips/Details", new { id = trip.Id });
    }

    private static string? Trimmed(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
