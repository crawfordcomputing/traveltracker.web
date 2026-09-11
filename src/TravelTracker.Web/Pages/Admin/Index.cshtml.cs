using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TravelTracker.Web.Data;
using TravelTracker.Web.Data.Entities;

namespace TravelTracker.Web.Pages.Admin;

public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly UserManager<AppUser> _userManager;
    public IndexModel(AppDbContext db, UserManager<AppUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    public int UserCount { get; private set; }
    public int DepartmentCount { get; private set; }
    public int PendingInviteCount { get; private set; }
    public int ArrangerCount { get; private set; }
    public int MileageRateCount { get; private set; }
    public int CostCenterCount { get; private set; }
    public int ProjectCodeCount { get; private set; }
    public int ExpensePolicyCount { get; private set; }
    public int EmailTemplateCount { get; private set; }

    public async Task OnGetAsync()
    {
        var now = DateTimeOffset.UtcNow;
        UserCount = await _db.Users.CountAsync();
        DepartmentCount = await _db.Departments.CountAsync();
        PendingInviteCount = await _db.Invitations
            .CountAsync(i => i.AcceptedAt == null && i.ExpiresAt > now);
        ArrangerCount = (await _userManager.GetUsersInRoleAsync(Roles.Arranger)).Count;
        MileageRateCount = await _db.MileageRates.CountAsync();
        CostCenterCount = await _db.CostCenters.CountAsync();
        ProjectCodeCount = await _db.ProjectCodes.CountAsync();
        ExpensePolicyCount = await _db.ExpensePolicies.CountAsync();
        EmailTemplateCount = await _db.EmailTemplates.CountAsync();
    }
}
