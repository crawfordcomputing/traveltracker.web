using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TravelTracker.Web.Data;
using TravelTracker.Web.Data.Entities;

namespace TravelTracker.Web.Pages.Admin.Notifications;

// Admin-only (inherits the /Admin RequireAdmin folder policy). Read-only view of
// outbound email outcomes so admins can confirm delivery and see failures without
// opening Application Insights. No message bodies / tokens are stored or shown.
public class IndexModel : PageModel
{
    private const int PageSize = 50;
    private readonly AppDbContext _db;

    public IndexModel(AppDbContext db) => _db = db;

    public IReadOnlyList<NotificationLog> Items { get; private set; } = Array.Empty<NotificationLog>();
    public int PageNumber { get; private set; }
    public bool HasNext { get; private set; }
    public int SentCount24h { get; private set; }
    public int FailedCount24h { get; private set; }

    [BindProperty(SupportsGet = true)] public NotificationStatus? Status { get; set; }
    [BindProperty(SupportsGet = true, Name = "page")] public int PageNo { get; set; } = 1;

    public async Task OnGetAsync()
    {
        PageNumber = PageNo < 1 ? 1 : PageNo;

        var since = DateTimeOffset.UtcNow.AddHours(-24);
        SentCount24h = await _db.NotificationLogs
            .CountAsync(n => n.SentAt >= since && n.Status == NotificationStatus.Sent);
        FailedCount24h = await _db.NotificationLogs
            .CountAsync(n => n.SentAt >= since && n.Status == NotificationStatus.Failed);

        var query = _db.NotificationLogs.AsNoTracking();
        if (Status is not null)
            query = query.Where(n => n.Status == Status);

        // Fetch one extra row to detect whether a next page exists.
        var rows = await query
            .OrderByDescending(n => n.SentAt)
            .Skip((PageNumber - 1) * PageSize)
            .Take(PageSize + 1)
            .ToListAsync();

        HasNext = rows.Count > PageSize;
        Items = rows.Take(PageSize).ToList();
    }
}
