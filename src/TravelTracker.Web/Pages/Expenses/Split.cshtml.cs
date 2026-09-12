using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TravelTracker.Web.Data;
using TravelTracker.Web.Data.Entities;
using TravelTracker.Web.Domain;

namespace TravelTracker.Web.Pages.Expenses;

// Manage same-trip category splits for one parent expense. No-JS, add-one-at-a-time
// (mirrors how Trips/Details adds legs). Splits carry frozen BaseAmounts; the page
// surfaces the running remainder and reconciliation state. Rollups only expand a
// split set once it reconciles to the parent (see ExpenseRollup), so an in-progress
// set is safe to persist.
public class SplitModel : ExpensePageModel
{
    public SplitModel(AppDbContext db, TeamAccess team) : base(db, team) { }

    public Expense Expense { get; private set; } = default!;
    public IReadOnlyList<ExpenseSplit> Splits { get; private set; } = Array.Empty<ExpenseSplit>();

    public decimal Allocated { get; private set; }
    public decimal Remaining { get; private set; }
    public bool IsReconciled { get; private set; }

    [BindProperty] public InputModel Input { get; set; } = new();

    public class InputModel
    {
        public ExpenseCategory Category { get; set; } = ExpenseCategory.Unspecified;

        [Required, Range(0.01, 1_000_000, ErrorMessage = "Enter an amount greater than zero.")]
        public decimal Amount { get; set; }

        [StringLength(500)]
        public string? Description { get; set; }
    }

    private async Task<Expense?> LoadAsync(int id)
    {
        var expense = await LoadAuthorizedExpenseAsync(id);
        if (expense is null) return null;

        Expense = expense;
        Splits = await Db.ExpenseSplits
            .Where(s => s.ExpenseId == expense.Id)
            .OrderBy(s => s.Id)
            .ToListAsync();

        Allocated = Splits.Sum(s => s.BaseAmount);
        Remaining = ExpenseSplitMath.Remainder(expense.BaseAmount, Splits.Select(s => s.BaseAmount));
        IsReconciled = ExpenseSplitMath.Reconciles(expense.BaseAmount, Splits.Select(s => s.BaseAmount));
        return expense;
    }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var expense = await LoadAsync(id);
        if (expense is null) return NotFound();

        // Prefill the add-row amount with whatever is still unallocated.
        if (Remaining > 0) Input.Amount = Remaining;
        return Page();
    }

    public async Task<IActionResult> OnPostAddAsync(int id)
    {
        var expense = await LoadAsync(id);
        if (expense is null) return NotFound();

        if (!ModelState.IsValid)
            return Page();

        // Freeze BaseAmount at the parent's rate (single-currency today ⇒ 1:1).
        Db.ExpenseSplits.Add(new ExpenseSplit
        {
            ExpenseId = expense.Id,
            Category = Input.Category,
            Amount = Input.Amount,
            BaseAmount = Input.Amount * expense.FxRate,
            Description = string.IsNullOrWhiteSpace(Input.Description) ? null : Input.Description.Trim()
        });
        await Db.SaveChangesAsync();
        return RedirectToPage("Split", new { id });
    }

    public async Task<IActionResult> OnPostRemoveAsync(int id, int splitId)
    {
        var expense = await LoadAsync(id);
        if (expense is null) return NotFound();

        var split = await Db.ExpenseSplits
            .FirstOrDefaultAsync(s => s.Id == splitId && s.ExpenseId == expense.Id);
        if (split is not null)
        {
            Db.ExpenseSplits.Remove(split);
            await Db.SaveChangesAsync();
        }
        return RedirectToPage("Split", new { id });
    }

    public async Task<IActionResult> OnPostClearAsync(int id)
    {
        var expense = await LoadAsync(id);
        if (expense is null) return NotFound();

        var all = await Db.ExpenseSplits.Where(s => s.ExpenseId == expense.Id).ToListAsync();
        if (all.Count > 0)
        {
            Db.ExpenseSplits.RemoveRange(all);
            await Db.SaveChangesAsync();
        }
        return RedirectToPage("Split", new { id });
    }
}
