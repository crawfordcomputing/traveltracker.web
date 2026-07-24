using System.Security.Claims;
using TravelTracker.Web.Data.Entities;
using TravelTracker.Web.Domain;
using Xunit;

namespace TravelTracker.Tests;

public class ReportAccessTests
{
    private static ClaimsPrincipal Principal(string userId, params string[] roles)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId) };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    private static IReadOnlySet<string> Set(params string[] ids) => ids.ToHashSet();

    [Fact]
    public void Finance_Sees_Org_Wide()
    {
        var finance = Principal("f1", Roles.Finance);
        Assert.True(ReportAccess.CanSeeOrgWide(finance));
        Assert.True(ReportAccess.Scope(finance, Set()).All);
    }

    [Fact]
    public void Admin_Sees_Org_Wide()
    {
        Assert.True(ReportAccess.CanSeeOrgWide(Principal("a1", Roles.Admin)));
        Assert.True(ReportAccess.Scope(Principal("a1", Roles.Admin), Set()).All);
    }

    [Fact]
    public void Manager_Is_Scoped_To_Self_Plus_Reachable()
    {
        var manager = Principal("m1", Roles.Manager);
        Assert.False(ReportAccess.CanSeeOrgWide(manager));

        var scope = ReportAccess.Scope(manager, Set("t1", "t2"));
        Assert.False(scope.All);
        Assert.Contains("m1", scope.TravelerIds); // self
        Assert.Contains("t1", scope.TravelerIds); // department teammate
        Assert.Contains("t2", scope.TravelerIds);
    }

    [Fact]
    public void Arranger_Is_Scoped_To_Self_Plus_Assigned()
    {
        var arranger = Principal("ar1", Roles.Arranger);
        Assert.False(ReportAccess.CanSeeOrgWide(arranger));

        var scope = ReportAccess.Scope(arranger, Set("t9"));
        Assert.False(scope.All);
        Assert.Contains("ar1", scope.TravelerIds);
        Assert.Contains("t9", scope.TravelerIds);
    }

    [Fact]
    public void Plain_Employee_Is_Self_Only()
    {
        var employee = Principal("e1", Roles.Employee);
        var scope = ReportAccess.Scope(employee, Set("x1")); // stray set ignored
        Assert.False(scope.All);
        Assert.Contains("e1", scope.TravelerIds);
        Assert.DoesNotContain("x1", scope.TravelerIds);
    }
}
