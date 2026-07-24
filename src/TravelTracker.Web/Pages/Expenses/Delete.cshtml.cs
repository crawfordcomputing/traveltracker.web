using Microsoft.AspNetCore.Mvc;
using TravelTracker.Web.Data;
using TravelTracker.Web.Data.Entities;
using TravelTracker.Web.Domain;
using TravelTracker.Web.Services;

namespace TravelTracker.Web.Pages.Expenses;

public class DeleteModel : ExpensePageModel
{
    private readonly IReceiptStorage _storage;

    public DeleteModel(AppDbContext db, TeamAccess team, IReceiptStorage storage)
        : base(db, team)
    {
        _storage = storage;
    }

    public Expense Expense { get; private set; } = default!;

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var expense = await LoadAuthorizedExpenseAsync(id);
        if (expense is null) return NotFound();
        Expense = expense;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        var expense = await LoadAuthorizedExpenseAsync(id);
        if (expense is null) return NotFound();

        var tripId = expense.TripId;
        if (!string.IsNullOrEmpty(expense.ReceiptPath))
            await _storage.DeleteAsync(expense.ReceiptPath);

        Db.Expenses.Remove(expense);
        await Db.SaveChangesAsync();
        return RedirectToPage("/Trips/Details", new { id = tripId });
    }
}
