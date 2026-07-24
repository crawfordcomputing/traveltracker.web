using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using TravelTracker.Web.Data;
using TravelTracker.Web.Data.Entities;

namespace TravelTracker.Web.Domain;

// Loads the traveler ids a user may act for *beyond themselves*, by virtue of a
// scoped role: a Manager reaches everyone in their department; an Arranger reaches
// their explicitly-assigned travelers (a user may be both — the sets union). Kept
// thin and DB-only so the access *decisions* stay in the pure TripAccess functions.
// A plain Employee (no scoped role) never touches the database — the set is empty.
public class TeamAccess
{
    private readonly AppDbContext _db;

    public TeamAccess(AppDbContext db) => _db = db;

    public async Task<IReadOnlySet<string>> ReachableTravelerIdsAsync(ClaimsPrincipal user)
    {
        var uid = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(uid))
            return EmptySet;

        var isManager = user.IsInRole(Roles.Manager);
        var isArranger = user.IsInRole(Roles.Arranger);
        if (!isManager && !isArranger)
            return EmptySet;

        var ids = new HashSet<string>();

        // Manager: every other member of their department. A manager with no
        // department (or alone in it) simply gets no teammates — fails closed.
        if (isManager)
        {
            var deptId = await _db.Users
                .Where(u => u.Id == uid)
                .Select(u => u.DepartmentId)
                .FirstOrDefaultAsync();

            if (deptId is not null)
            {
                var deptMates = await _db.Users
                    .Where(u => u.DepartmentId == deptId && u.Id != uid)
                    .Select(u => u.Id)
                    .ToListAsync();
                ids.UnionWith(deptMates);
            }
        }

        // Arranger: explicitly-assigned travelers.
        if (isArranger)
        {
            var assigned = await _db.ArrangerAssignments
                .Where(a => a.ArrangerId == uid)
                .Select(a => a.TravelerId)
                .ToListAsync();
            ids.UnionWith(assigned);
        }

        return ids;
    }

    private static readonly IReadOnlySet<string> EmptySet = new HashSet<string>();
}
