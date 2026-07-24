using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TravelTracker.Web.Data;
using TravelTracker.Web.Data.Entities;
using TravelTracker.Web.Domain;
using Xunit;

namespace TravelTracker.Tests;

// Covers the scoped delegated roles fed by the TeamAccess lookup: an Arranger's
// assigned travelers (Admin/Arrangers assign/unassign + Trips/Index surfacing self +
// assigned) and a Manager's department teammates (M1.5: managers see their team, not
// every traveler).
public class ArrangerAccessTests
{
    private static ServiceProvider BuildServices(string dbFile)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = TestDatabase.ConnectionStringFor(dbFile)
            }).Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddLogging();
        services.AddDataProtection();
        services.AddHttpContextAccessor();
        services.AddAuthentication();
        services.AddAppDatabase(config);
        services.AddIdentityCore<AppUser>(o =>
            {
                o.Password.RequiredLength = 8;
                o.Password.RequireNonAlphanumeric = false;
                o.Password.RequireUppercase = false;
                o.User.RequireUniqueEmail = true;
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders();
        services.AddScoped<TeamAccess>();

        return services.BuildServiceProvider();
    }

    private static void Cleanup(string dbFile)
    {
        TestDatabase.Drop(TestDatabase.ConnectionStringFor(dbFile));
    }

    private static async Task<AppUser> CreateUserAsync(
        UserManager<AppUser> um, string email, string? role = null, int? departmentId = null)
    {
        var user = new AppUser
        {
            UserName = email, Email = email, DisplayName = email,
            EmailConfirmed = true, DepartmentId = departmentId,
        };
        var result = await um.CreateAsync(user, "Passw0rd!");
        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(e => e.Description)));
        if (role is not null) await um.AddToRoleAsync(user, role);
        return user;
    }

    private static ClaimsPrincipal Principal(string userId, params string[] roles)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId) };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    private static void Add(AppDbContext db, string travelerId)
        => db.Trips.Add(new Trip
        {
            TravelerId = travelerId,
            Purpose = "Trip",
            Status = TripStatus.Planned,
            StartDate = DateOnly.FromDateTime(DateTime.Today),
            EndDate = DateOnly.FromDateTime(DateTime.Today).AddDays(3),
            CreatedById = travelerId
        });

    [Fact]
    public async Task Delegated_Ids_Returned_For_Arranger_Empty_For_Others()
    {
        var dbFile = Path.Combine(Path.GetTempPath(), $"tt-arr-{Guid.NewGuid():N}.db");
        var sp = BuildServices(dbFile);
        try
        {
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var um = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var rm = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            await db.Database.EnsureCreatedAsync();
            foreach (var r in Roles.All) await rm.CreateAsync(new IdentityRole(r));

            var arranger = await CreateUserAsync(um, "arr@example.com", Roles.Arranger);
            var t1 = await CreateUserAsync(um, "t1@example.com", Roles.Employee);
            await CreateUserAsync(um, "t2@example.com", Roles.Employee);
            db.ArrangerAssignments.Add(new ArrangerAssignment { ArrangerId = arranger.Id, TravelerId = t1.Id });
            await db.SaveChangesAsync();

            var svc = new TeamAccess(db);

            var forArranger = await svc.ReachableTravelerIdsAsync(Principal(arranger.Id, Roles.Arranger));
            Assert.Equal(new[] { t1.Id }, forArranger);

            // Arranger assignments are never handed to a non-arranger. This user has
            // no department either, so as a Manager they reach nobody. (Department
            // reach for a Manager is covered by Manager_Reaches_Department_Teammates_Only.)
            var forManager = await svc.ReachableTravelerIdsAsync(Principal(arranger.Id, Roles.Manager));
            Assert.Empty(forManager);
        }
        finally { await sp.DisposeAsync(); Cleanup(dbFile); }
    }

    [Fact]
    public async Task Admin_Can_Assign_Then_Unassign_A_Traveler()
    {
        var dbFile = Path.Combine(Path.GetTempPath(), $"tt-arr-{Guid.NewGuid():N}.db");
        var sp = BuildServices(dbFile);
        try
        {
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var um = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var rm = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            await db.Database.EnsureCreatedAsync();
            foreach (var r in Roles.All) await rm.CreateAsync(new IdentityRole(r));

            var arranger = await CreateUserAsync(um, "arr@example.com", Roles.Arranger);
            var traveler = await CreateUserAsync(um, "t1@example.com", Roles.Employee);

            var page = NewArrangersPage(scope.ServiceProvider, db, um);
            page.ArrangerId = arranger.Id;
            page.TravelerId = traveler.Id;
            var assign = await page.OnPostAssignAsync();
            Assert.IsType<RedirectToPageResult>(assign);

            var row = await db.ArrangerAssignments.SingleAsync();
            Assert.Equal(arranger.Id, row.ArrangerId);
            Assert.Equal(traveler.Id, row.TravelerId);

            var unassign = await page.OnPostUnassignAsync(row.Id);
            Assert.IsType<RedirectToPageResult>(unassign);
            Assert.False(await db.ArrangerAssignments.AnyAsync());
        }
        finally { await sp.DisposeAsync(); Cleanup(dbFile); }
    }

    [Fact]
    public async Task Assigning_A_Non_Arranger_Is_Rejected()
    {
        var dbFile = Path.Combine(Path.GetTempPath(), $"tt-arr-{Guid.NewGuid():N}.db");
        var sp = BuildServices(dbFile);
        try
        {
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var um = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var rm = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            await db.Database.EnsureCreatedAsync();
            foreach (var r in Roles.All) await rm.CreateAsync(new IdentityRole(r));

            var notArranger = await CreateUserAsync(um, "emp@example.com", Roles.Employee);
            var traveler = await CreateUserAsync(um, "t1@example.com", Roles.Employee);

            var page = NewArrangersPage(scope.ServiceProvider, db, um);
            page.ArrangerId = notArranger.Id;
            page.TravelerId = traveler.Id;
            await page.OnPostAssignAsync();

            Assert.NotNull(page.TempData["Error"]);
            Assert.False(await db.ArrangerAssignments.AnyAsync());
        }
        finally { await sp.DisposeAsync(); Cleanup(dbFile); }
    }

    [Fact]
    public async Task Trips_Index_Shows_Self_And_Assigned_Only()
    {
        var dbFile = Path.Combine(Path.GetTempPath(), $"tt-arr-{Guid.NewGuid():N}.db");
        var sp = BuildServices(dbFile);
        try
        {
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var um = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var rm = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var arrangerSvc = scope.ServiceProvider.GetRequiredService<TeamAccess>();
            await db.Database.EnsureCreatedAsync();
            foreach (var r in Roles.All) await rm.CreateAsync(new IdentityRole(r));

            var arranger = await CreateUserAsync(um, "arr@example.com", Roles.Arranger);
            var assigned = await CreateUserAsync(um, "t1@example.com", Roles.Employee);
            var other = await CreateUserAsync(um, "t2@example.com", Roles.Employee);
            db.ArrangerAssignments.Add(new ArrangerAssignment { ArrangerId = arranger.Id, TravelerId = assigned.Id });
            Add(db, arranger.Id);
            Add(db, assigned.Id);
            Add(db, other.Id);   // must NOT be visible
            await db.SaveChangesAsync();

            var page = NewTripsIndex(scope.ServiceProvider, db, um, arrangerSvc,
                Principal(arranger.Id, Roles.Arranger));
            page.When = "all";
            await page.OnGetAsync();

            var visible = page.Trips.Select(t => t.TravelerId).ToHashSet();
            Assert.Contains(arranger.Id, visible);
            Assert.Contains(assigned.Id, visible);
            Assert.DoesNotContain(other.Id, visible);
            Assert.True(page.ShowTraveler);
        }
        finally { await sp.DisposeAsync(); Cleanup(dbFile); }
    }

    [Fact]
    public async Task Manager_Reaches_Department_Teammates_Only()
    {
        var dbFile = Path.Combine(Path.GetTempPath(), $"tt-mgr-{Guid.NewGuid():N}.db");
        var sp = BuildServices(dbFile);
        try
        {
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var um = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var rm = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            await db.Database.EnsureCreatedAsync();
            foreach (var r in Roles.All) await rm.CreateAsync(new IdentityRole(r));

            var eng = new Department { Name = "Engineering" };
            var sales = new Department { Name = "Sales" };
            db.Departments.AddRange(eng, sales);
            await db.SaveChangesAsync();

            var manager = await CreateUserAsync(um, "mgr@example.com", Roles.Manager, eng.Id);
            var mate1 = await CreateUserAsync(um, "e1@example.com", Roles.Employee, eng.Id);
            var mate2 = await CreateUserAsync(um, "e2@example.com", Roles.Employee, eng.Id);
            var otherDept = await CreateUserAsync(um, "s1@example.com", Roles.Employee, sales.Id);

            var svc = new TeamAccess(db);
            var reachable = await svc.ReachableTravelerIdsAsync(Principal(manager.Id, Roles.Manager));

            // Department teammates, excluding the manager themselves and other departments.
            Assert.Contains(mate1.Id, reachable);
            Assert.Contains(mate2.Id, reachable);
            Assert.DoesNotContain(manager.Id, reachable);
            Assert.DoesNotContain(otherDept.Id, reachable);
        }
        finally { await sp.DisposeAsync(); Cleanup(dbFile); }
    }

    [Fact]
    public async Task Trips_Index_Scopes_Manager_To_Department()
    {
        var dbFile = Path.Combine(Path.GetTempPath(), $"tt-mgr-{Guid.NewGuid():N}.db");
        var sp = BuildServices(dbFile);
        try
        {
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var um = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var rm = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var teamSvc = scope.ServiceProvider.GetRequiredService<TeamAccess>();
            await db.Database.EnsureCreatedAsync();
            foreach (var r in Roles.All) await rm.CreateAsync(new IdentityRole(r));

            var eng = new Department { Name = "Engineering" };
            var sales = new Department { Name = "Sales" };
            db.Departments.AddRange(eng, sales);
            await db.SaveChangesAsync();

            var manager = await CreateUserAsync(um, "mgr@example.com", Roles.Manager, eng.Id);
            var teammate = await CreateUserAsync(um, "e1@example.com", Roles.Employee, eng.Id);
            var outsider = await CreateUserAsync(um, "s1@example.com", Roles.Employee, sales.Id);
            Add(db, manager.Id);
            Add(db, teammate.Id);
            Add(db, outsider.Id);   // other department — must NOT be visible
            await db.SaveChangesAsync();

            var page = NewTripsIndex(scope.ServiceProvider, db, um, teamSvc,
                Principal(manager.Id, Roles.Manager));
            page.When = "all";
            await page.OnGetAsync();

            var visible = page.Trips.Select(t => t.TravelerId).ToHashSet();
            Assert.Contains(manager.Id, visible);
            Assert.Contains(teammate.Id, visible);
            Assert.DoesNotContain(outsider.Id, visible);
            Assert.True(page.ShowTraveler);      // sees teammates, so the traveler column shows
            Assert.True(page.CanSeeTeamRoster);  // still a manager: sees the "Who's out" roster
        }
        finally { await sp.DisposeAsync(); Cleanup(dbFile); }
    }

    private static Web.Pages.Admin.Arrangers.IndexModel NewArrangersPage(
        IServiceProvider sp, AppDbContext db, UserManager<AppUser> um)
    {
        var httpContext = new DefaultHttpContext { RequestServices = sp };
        var actionContext = new ActionContext(
            httpContext, new RouteData(), new PageActionDescriptor(), new ModelStateDictionary());
        return new Web.Pages.Admin.Arrangers.IndexModel(db, um)
        {
            PageContext = new PageContext(actionContext),
            TempData = new TempDataDictionary(httpContext, new TestTempDataProvider())
        };
    }

    private static Web.Pages.Trips.IndexModel NewTripsIndex(
        IServiceProvider sp, AppDbContext db, UserManager<AppUser> um,
        TeamAccess arranger, ClaimsPrincipal user)
    {
        var httpContext = new DefaultHttpContext { RequestServices = sp, User = user };
        var actionContext = new ActionContext(
            httpContext, new RouteData(), new PageActionDescriptor(), new ModelStateDictionary());
        return new Web.Pages.Trips.IndexModel(db, um, arranger)
        {
            PageContext = new PageContext(actionContext),
            TempData = new TempDataDictionary(httpContext, new TestTempDataProvider())
        };
    }

    private sealed class TestTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) =>
            new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
}
