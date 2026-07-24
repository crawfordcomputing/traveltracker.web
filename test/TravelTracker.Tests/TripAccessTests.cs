using System.Security.Claims;
using TravelTracker.Web.Data.Entities;
using TravelTracker.Web.Domain;
using Xunit;

namespace TravelTracker.Tests;

public class TripAccessTests
{
    private static ClaimsPrincipal Principal(string userId, params string[] roles)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId) };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    [Fact]
    public void Owner_Can_Access_Own_Trip()
    {
        var trip = new Trip { TravelerId = "u1" };
        Assert.True(TripAccess.CanAccess(Principal("u1"), trip));
    }

    [Fact]
    public void Employee_Cannot_Access_Others_Trip()
    {
        var trip = new Trip { TravelerId = "u1" };
        Assert.False(TripAccess.CanAccess(Principal("u2"), trip));
    }

    [Fact]
    public void Admin_Can_Access_Any_Trip_Globally()
    {
        var trip = new Trip { TravelerId = "u1" };
        // No reachable set supplied: Admin still reaches every trip.
        Assert.True(TripAccess.CanAccess(Principal("u2", Roles.Admin), trip));
    }

    private static IReadOnlySet<string> Set(params string[] ids) => ids.ToHashSet();

    [Fact]
    public void Manager_Is_Scoped_To_Their_Reachable_Team()
    {
        // A Manager reaches only travelers in the supplied set (their department),
        // never every trip in the org — that global reach is now Admin-only.
        var trip = new Trip { TravelerId = "u1" };
        var manager = Principal("u2", Roles.Manager);

        Assert.True(TripAccess.CanAccess(manager, trip, Set("u1")));   // teammate
        Assert.False(TripAccess.CanAccess(manager, trip, Set("u9")));  // other dept
        Assert.False(TripAccess.CanAccess(manager, trip));             // no team set
    }

    [Fact]
    public void Manager_Always_Reaches_Their_Own_Trip()
    {
        var trip = new Trip { TravelerId = "u2" };
        Assert.True(TripAccess.CanAccess(Principal("u2", Roles.Manager), trip));
    }

    [Fact]
    public void Arranger_Can_Access_Assigned_Traveler_Only()
    {
        var trip = new Trip { TravelerId = "u1" };
        var arranger = Principal("u2", Roles.Arranger);

        Assert.True(TripAccess.CanAccess(arranger, trip, Set("u1")));      // assigned
        Assert.False(TripAccess.CanAccess(arranger, trip, Set("u9")));     // not assigned
        Assert.False(TripAccess.CanAccess(arranger, trip));                // no delegated set
    }

    [Fact]
    public void Delegated_Set_Does_Not_Help_A_Non_Arranger()
    {
        // A plain employee handed a delegated set (defensive) still can't reach others.
        var trip = new Trip { TravelerId = "u1" };
        Assert.False(TripAccess.CanAccess(Principal("u2"), trip, Set("u1")));
    }

    [Fact]
    public void Arranger_Always_Reaches_Their_Own_Trip()
    {
        var trip = new Trip { TravelerId = "u2" };
        Assert.True(TripAccess.CanAccess(Principal("u2", Roles.Arranger), trip));
    }

    [Fact]
    public void Scope_Is_Everyone_For_Admin_Only()
    {
        var scope = TripAccess.Scope(Principal("u2", Roles.Admin), Set("ignored"));
        Assert.True(scope.All);
        Assert.True(scope.Includes("anyone"));
    }

    [Fact]
    public void Scope_Is_Self_Plus_Team_For_Manager()
    {
        // A Manager is department-scoped, not global: self + supplied teammates.
        var scope = TripAccess.Scope(Principal("u2", Roles.Manager), Set("u1", "u3"));
        Assert.False(scope.All);
        Assert.Equal(new[] { "u1", "u2", "u3" }, scope.TravelerIds.OrderBy(x => x));
        Assert.True(scope.Includes("u1"));
        Assert.False(scope.Includes("u9"));
    }

    [Theory]
    [InlineData(Roles.Manager, true)]
    [InlineData(Roles.Admin, true)]
    [InlineData(Roles.Arranger, false)]
    [InlineData(Roles.Employee, false)]
    public void Team_Roster_Is_Manager_Or_Admin(string role, bool expected)
    {
        Assert.Equal(expected, TripAccess.CanSeeTeamRoster(Principal("u1", role)));
    }

    [Theory]
    [InlineData(Roles.Admin, true)]
    [InlineData(Roles.Manager, false)]
    [InlineData(Roles.Employee, false)]
    public void Global_Management_Is_Admin_Only(string role, bool expected)
    {
        Assert.Equal(expected, TripAccess.CanManageGlobally(Principal("u1", role)));
    }

    [Fact]
    public void Scope_Is_Self_Only_For_Employee()
    {
        var scope = TripAccess.Scope(Principal("u2"), Set("u1"));
        Assert.False(scope.All);
        Assert.Equal(new[] { "u2" }, scope.TravelerIds);
        Assert.True(scope.Includes("u2"));
        Assert.False(scope.Includes("u1"));
    }

    [Fact]
    public void Scope_Is_Self_Plus_Delegated_For_Arranger()
    {
        var scope = TripAccess.Scope(Principal("u2", Roles.Arranger), Set("u1", "u3"));
        Assert.False(scope.All);
        Assert.Equal(new[] { "u1", "u2", "u3" }, scope.TravelerIds.OrderBy(x => x));
        Assert.True(scope.Includes("u1"));
        Assert.False(scope.Includes("u9"));
    }
}
