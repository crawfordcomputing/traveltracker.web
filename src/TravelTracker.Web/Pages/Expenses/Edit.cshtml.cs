using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TravelTracker.Web.Data;
using TravelTracker.Web.Data.Entities;
using TravelTracker.Web.Domain;
using TravelTracker.Web.Services;

namespace TravelTracker.Web.Pages.Expenses;

public class EditModel : ExpensePageModel
{
    private readonly IReceiptStorage _storage;
    private readonly IConfiguration _config;

    public EditModel(AppDbContext db, TeamAccess team, IReceiptStorage storage, IConfiguration config)
        : base(db, team)
    {
        _storage = storage;
        _config = config;
    }

    public int ExpenseId { get; private set; }
    public int TripId { get; private set; }
    public string TripPurpose { get; private set; } = string.Empty;
    public bool HasReceipt { get; private set; }
    public string Currency { get; private set; } = "USD";

    [BindProperty] public InputModel Input { get; set; } = new();
    [BindProperty] public IFormFile? Receipt { get; set; }
    [BindProperty] public bool RemoveReceipt { get; set; }
    [BindProperty] public bool ConfirmDuplicate { get; set; }

    public IReadOnlyList<Expense> DuplicateMatches { get; private set; } = Array.Empty<Expense>();

    private int MaxReceiptMb => _config.GetValue("Storage:MaxReceiptMb", 10);
    public bool ReceiptRequired => _config.GetValue("Expenses:ReceiptRequired", false);
    public decimal ReceiptThreshold => _config.GetValue("Expenses:ReceiptThreshold", 25m);

    public class InputModel
    {
        public ExpenseCategory Category { get; set; } = ExpenseCategory.Unspecified;

        [Required, DataType(DataType.Date)]
        public DateOnly Date { get; set; }

        [StringLength(160)]
        public string? Vendor { get; set; }

        [StringLength(500)]
        public string? Description { get; set; }

        [Required, Range(0.01, 1_000_000, ErrorMessage = "Enter an amount greater than zero.")]
        public decimal Amount { get; set; }

        [Display(Name = "Personal / non-reimbursable")]
        public bool IsPersonal { get; set; }

        [StringLength(500)]
        public string? Attendees { get; set; }

        [StringLength(500), Display(Name = "Business purpose")]
        public string? BusinessPurpose { get; set; }

        [StringLength(500), Display(Name = "Missing-receipt affidavit")]
        public string? MissingReceiptAffidavit { get; set; }

        // Lightweight reimbursement status, independent of trip status (M6 owns the
        // full approval flow).
        public bool Reimbursed { get; set; }

        [DataType(DataType.Date), Display(Name = "Reimbursed on")]
        public DateOnly? ReimbursedDate { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var expense = await LoadAuthorizedExpenseAsync(id);
        if (expense is null) return NotFound();

        Bind(expense);
        Input = new InputModel
        {
            Category = expense.Category,
            Date = expense.Date,
            Vendor = expense.Vendor,
            Description = expense.Description,
            Amount = expense.Amount,
            IsPersonal = expense.IsPersonal,
            Attendees = expense.Attendees,
            BusinessPurpose = expense.BusinessPurpose,
            MissingReceiptAffidavit = expense.MissingReceiptAffidavit,
            Reimbursed = expense.Reimbursed,
            ReimbursedDate = expense.ReimbursedDate
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        var expense = await LoadAuthorizedExpenseAsync(id);
        if (expense is null) return NotFound();
        Bind(expense);

        if (Receipt is not null)
        {
            var error = ReceiptUpload.Validate(
                Receipt.FileName, Receipt.ContentType, Receipt.Length, MaxReceiptMb);
            if (error is not null)
                ModelState.AddModelError(nameof(Receipt), error);
        }

        // A reimbursed date without the flag (or vice versa) is almost certainly a
        // mistake — keep the pair coherent.
        if (Input.ReimbursedDate is not null && !Input.Reimbursed)
            ModelState.AddModelError(nameof(Input) + "." + nameof(InputModel.Reimbursed),
                "Tick 'Reimbursed' to record a reimbursement date.");

        if (!ModelState.IsValid)
            return Page();

        // Advisory duplicate check against OTHER lines on the same trip.
        var candidate = new Expense { Id = expense.Id, Amount = Input.Amount, Date = Input.Date, Vendor = Input.Vendor };
        var others = await Db.Expenses
            .Where(e => e.TripId == expense.TripId && e.Id != expense.Id)
            .ToListAsync();
        var matches = DuplicateExpenseCheck.FindMatches(candidate, others);
        if (matches.Count > 0 && !ConfirmDuplicate)
        {
            DuplicateMatches = matches;
            return Page();
        }

        // Receipt: replace (new upload wins, delete old) or explicit remove.
        if (Receipt is not null)
        {
            var oldKey = expense.ReceiptPath;
            await using var stream = Receipt.OpenReadStream();
            expense.ReceiptPath = await _storage.SaveAsync(stream, Receipt.FileName, Receipt.ContentType);
            if (!string.IsNullOrEmpty(oldKey))
                await _storage.DeleteAsync(oldKey);
        }
        else if (RemoveReceipt && !string.IsNullOrEmpty(expense.ReceiptPath))
        {
            await _storage.DeleteAsync(expense.ReceiptPath);
            expense.ReceiptPath = null;
        }

        expense.Category = Input.Category;
        expense.Date = Input.Date;
        expense.Vendor = Trimmed(Input.Vendor);
        expense.Description = Trimmed(Input.Description);
        expense.Amount = Input.Amount;
        expense.IsPersonal = Input.IsPersonal;
        expense.Attendees = Trimmed(Input.Attendees);
        expense.BusinessPurpose = Trimmed(Input.BusinessPurpose);
        // An affidavit only makes sense while there's no receipt.
        expense.MissingReceiptAffidavit = string.IsNullOrEmpty(expense.ReceiptPath)
            ? Trimmed(Input.MissingReceiptAffidavit)
            : null;
        expense.Reimbursed = Input.Reimbursed;
        expense.ReimbursedDate = Input.Reimbursed ? Input.ReimbursedDate : null;
        // Re-freeze the base value. Single-currency here (FxRate stays 1).
        expense.BaseAmount = Input.Amount * expense.FxRate;

        await Db.SaveChangesAsync();
        return RedirectToPage("/Trips/Details", new { id = expense.TripId });
    }

    private void Bind(Expense expense)
    {
        ExpenseId = expense.Id;
        TripId = expense.TripId;
        TripPurpose = expense.Trip?.Purpose ?? string.Empty;
        HasReceipt = !string.IsNullOrEmpty(expense.ReceiptPath);
        Currency = expense.Currency;
    }

    private static string? Trimmed(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
