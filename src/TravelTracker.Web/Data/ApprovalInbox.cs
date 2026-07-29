using Microsoft.EntityFrameworkCore;
using TravelTracker.Web.Data.Entities;

namespace TravelTracker.Web.Data;

// Small scoped helper for the approver's queue: "trips submitted to me, awaiting my
// decision." Used by the nav badge (_Layout) and the Approvals page so both agree on
// what counts as pending. Approver membership is the AppUser.ApproverId relationship,
// not a role — so this is a data question, kept out of the pure Domain layer.
public class ApprovalInbox
{
    private readonly AppDbContext _db;

    public ApprovalInbox(AppDbContext db) => _db = db;

    // True if anyone reports to this user as their approver (drives whether the nav
    // link shows at all, even when nothing is currently pending).
    public Task<bool> IsApproverAsync(string userId) =>
        _db.Users.AnyAsync(u => u.ApproverId == userId);

    // Count of trips submitted to this user and still awaiting a decision.
    public Task<int> PendingCountAsync(string userId) =>
        _db.Trips.CountAsync(t =>
            t.Status == TripStatus.Submitted &&
            t.Traveler!.ApproverId == userId);

    // The pending trips themselves, oldest start date first.
    public Task<List<Trip>> PendingAsync(string userId) =>
        _db.Trips
            .Include(t => t.Traveler)
            .Include(t => t.Destinations)
            .Where(t => t.Status == TripStatus.Submitted && t.Traveler!.ApproverId == userId)
            .OrderBy(t => t.StartDate)
            .ThenBy(t => t.Id)
            .ToListAsync();
}
