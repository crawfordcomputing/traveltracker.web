using Microsoft.AspNetCore.Identity;
using TravelTracker.Web.Data.Entities;

namespace TravelTracker.Web.Domain;

// Guards against locking everyone out: an action (deactivating or demoting) must
// not remove the final *active* Admin. Kept here (not inline in a page) so it is
// unit-testable and shared by Users/Index (deactivate) and Users/Edit (demote).
public static class AdminGuard
{
    // True when `candidate` is the last active Admin, i.e. deactivating or
    // demoting them would leave the system with no active administrator.
    // Inactive candidates never count — demoting an already-disabled admin is safe.
    public static async Task<bool> IsLastActiveAdminAsync(
        UserManager<AppUser> userManager, AppUser candidate)
    {
        var admins = await userManager.GetUsersInRoleAsync(Roles.Admin);
        return admins.Count(a => a.IsActive) <= 1
            && admins.Any(a => a.Id == candidate.Id && a.IsActive);
    }
}
