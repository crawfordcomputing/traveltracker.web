using System.Security.Claims;
using TravelTracker.Web.Data.Entities;

namespace TravelTracker.Web.Domain;

// Who may see aggregate spend reports, and over which travelers. Reporting is a
// finance/management surface, so reach is broader than a single trip:
//   - Admin or Finance  => org-wide (every traveler).
//   - Manager / Arranger => scoped to their reachable travelers (department teammates
//     / assignments), supplied by TeamAccess — same set the trip lists use.
//   - Anyone else        => self only.
// Pure function (no DB), mirroring TripAccess. The RequireReports policy keeps plain
// Employees off the pages entirely; this decides the data scope for those allowed in.
public static class ReportAccess
{
    // Org-wide reach: the whole firm's spend. Finance gets this without the account /
    // system god-mode that stays Admin-only.
    public static bool CanSeeOrgWide(ClaimsPrincipal user) =>
        user.IsInRole(Roles.Admin) || user.IsInRole(Roles.Finance);

    // The traveler-visibility scope for report queries. Org-wide roles => Everyone;
    // everyone else reuses the trip scope (self + Manager/Arranger reachable set).
    public static TripScope Scope(ClaimsPrincipal user, IReadOnlySet<string> reachableTravelerIds)
        => CanSeeOrgWide(user)
            ? TripScope.Everyone
            : TripAccess.Scope(user, reachableTravelerIds);
}
