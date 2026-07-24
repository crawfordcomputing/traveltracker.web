using System.Security.Claims;
using TravelTracker.Web.Data.Entities;

namespace TravelTracker.Web.Domain;

// Who may see and edit a given trip. Four tiers:
//   - Travelers own their own trips.
//   - Admins act on anyone's (global, org-wide reach).
//   - Managers act for their department; Arrangers for their assigned travelers.
//     Both are *scoped* — the caller supplies that reachable-traveler set (loaded by
//     TeamAccess), so this stays a pure function (no DB here).
public static class TripAccess
{
    // Global, org-wide reach. Admin only — Managers are department-scoped (below).
    public static bool CanManageGlobally(ClaimsPrincipal user) =>
        user.IsInRole(Roles.Admin);

    // The duty-of-care team roster ("Who's out") is a management view: Manager/Admin,
    // not Arrangers. Managers see it scoped to their department; Admins see everyone.
    public static bool CanSeeTeamRoster(ClaimsPrincipal user) =>
        user.IsInRole(Roles.Manager) || user.IsInRole(Roles.Admin);

    // Roles whose supplied reachable-traveler set is honoured: a Manager (their
    // department) or an Arranger (their assignments). A plain Employee is self-only,
    // so a stray set handed to one is ignored (defence in depth).
    private static bool HasScopedReach(ClaimsPrincipal user) =>
        user.IsInRole(Roles.Manager) || user.IsInRole(Roles.Arranger);

    public static bool CanAccess(
        ClaimsPrincipal user, Trip trip, IReadOnlySet<string>? reachableTravelerIds = null)
    {
        var uid = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (trip.TravelerId == uid) return true;      // own trip
        if (CanManageGlobally(user)) return true;     // Admin: global
        return HasScopedReach(user)                   // Manager/Arranger: only reachable
            && reachableTravelerIds is not null
            && reachableTravelerIds.Contains(trip.TravelerId);
    }

    // The set of travelers the caller may see/act for, for list/calendar queries.
    // Admin => everyone (All); everyone else => self plus, for a Manager/Arranger,
    // their reachable travelers (department teammates / assignments).
    public static TripScope Scope(ClaimsPrincipal user, IReadOnlySet<string> reachableTravelerIds)
    {
        if (CanManageGlobally(user))
            return TripScope.Everyone;

        var ids = new HashSet<string>();
        var uid = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (uid is not null) ids.Add(uid);
        if (HasScopedReach(user)) ids.UnionWith(reachableTravelerIds);
        return new TripScope(false, ids);
    }
}

// A caller's trip visibility: either everyone (Admin) or an explicit set of traveler
// ids. Filter a query with `if (!scope.All) q.Where(t => scope.TravelerIds
// .Contains(t.TravelerId))`, and check a single trip with `scope.Includes(id)`.
public sealed record TripScope(bool All, IReadOnlySet<string> TravelerIds)
{
    public static readonly TripScope Everyone =
        new(true, new HashSet<string>());

    public bool Includes(string travelerId) => All || TravelerIds.Contains(travelerId);
}
