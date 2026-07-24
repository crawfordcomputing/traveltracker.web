using Microsoft.AspNetCore.Mvc;
using TravelTracker.Web.Data;
using TravelTracker.Web.Domain;
using TravelTracker.Web.Services;

namespace TravelTracker.Web.Pages.Expenses;

// Streams a receipt file, authorized THROUGH the parent trip's scope. Receipts are
// PII, so they are never a static/public path — the only way to read one is this
// handler, which reuses the exact trip access check every other expense page uses.
public class ReceiptModel : ExpensePageModel
{
    private readonly IReceiptStorage _storage;

    public ReceiptModel(AppDbContext db, TeamAccess team, IReceiptStorage storage)
        : base(db, team)
    {
        _storage = storage;
    }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var expense = await LoadAuthorizedExpenseAsync(id);
        if (expense is null || string.IsNullOrEmpty(expense.ReceiptPath))
            return NotFound();

        var content = await _storage.OpenReadAsync(expense.ReceiptPath);
        if (content is null)
            return NotFound();

        // Inline (no download filename) so PDFs/images render in the browser. The
        // framework disposes the stream after the response is written.
        return File(content.Value.Stream, content.Value.ContentType);
    }
}
