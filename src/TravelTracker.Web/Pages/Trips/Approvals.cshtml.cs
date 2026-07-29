using System.Security.Claims;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TravelTracker.Web.Data;
using TravelTracker.Web.Data.Entities;

namespace TravelTracker.Web.Pages.Trips;

// The approver's work queue: trips submitted to me that are awaiting my decision.
// "Approver" is the AppUser.ApproverId relationship, not a role, so the /Trips
// folder auth (any authenticated user) is enough — the query itself scopes the list
// to this user's assignees. Deciding happens on Trips/Details via its approve/reject
// handlers, which re-check authorization.
public class ApprovalsModel : PageModel
{
    private readonly ApprovalInbox _queue;

    public ApprovalsModel(ApprovalInbox queue) => _queue = queue;

    public IReadOnlyList<Trip> Pending { get; private set; } = Array.Empty<Trip>();

    public async Task OnGetAsync()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is not null)
            Pending = await _queue.PendingAsync(userId);
    }
}
