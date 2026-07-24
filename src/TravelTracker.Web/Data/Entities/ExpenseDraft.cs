using System.ComponentModel.DataAnnotations;

namespace TravelTracker.Web.Data.Entities;

// Scratch autosave row for the "Add expense" form: one live draft per user per
// trip. Progressive enhancement only — a small vanilla-JS snippet upserts this as
// the user types, and the row is deleted when the real Expense is saved or the
// draft is discarded. The form works fully without JS; this never becomes an
// Expense on its own.
public class ExpenseDraft
{
    public int Id { get; set; }

    // Owner of the draft (the person editing the form), and the trip it targets.
    [Required]
    public string UserId { get; set; } = default!;
    public AppUser? User { get; set; }

    public int TripId { get; set; }
    public Trip? Trip { get; set; }

    public ExpenseCategory Category { get; set; } = ExpenseCategory.Unspecified;

    [DataType(DataType.Date)]
    public DateOnly? Date { get; set; }

    [StringLength(160)]
    public string? Vendor { get; set; }

    [StringLength(500)]
    public string? Description { get; set; }

    public decimal? Amount { get; set; }

    public bool IsPersonal { get; set; }

    [StringLength(500)]
    public string? Attendees { get; set; }

    [StringLength(500)]
    public string? BusinessPurpose { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
