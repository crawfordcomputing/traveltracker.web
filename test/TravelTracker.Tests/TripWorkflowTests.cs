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
using TravelTracker.Web.Pages.Trips;
using Xunit;

namespace TravelTracker.Tests;

// Drives the real Create/Details page handlers end to end: create a trip, add
// multi-leg destinations, then walk the status lifecycle to Completed.
public class TripWorkflowTests
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
        services.AddAppDatabase(config);
        services.AddIdentityCore<AppUser>(o =>
            {
                o.Password.RequiredLength = 8;
                o.Password.RequireNonAlphanumeric = false;
                o.Password.RequireUppercase = false;
                o.User.RequireUniqueEmail = true;
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<AppDbContext>();

        return services.BuildServiceProvider();
    }

    private static ClaimsPrincipal Principal(string userId, params string[] roles)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId) };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    private static (PageContext ctx, TempDataDictionary temp) PageCtx(IServiceProvider sp, ClaimsPrincipal user)
    {
        var http = new DefaultHttpContext { RequestServices = sp, User = user };
        var ac = new ActionContext(http, new RouteData(), new PageActionDescriptor(), new ModelStateDictionary());
        return (new PageContext(ac), new TempDataDictionary(http, new TestTempDataProvider()));
    }

    private static void Cleanup(string dbFile)
    {
        TestDatabase.Drop(TestDatabase.ConnectionStringFor(dbFile));
    }

    [Fact]
    public async Task Create_AddLegs_And_Complete_Flow()
    {
        var dbFile = Path.Combine(Path.GetTempPath(), $"tt-trip-{Guid.NewGuid():N}.db");
        var sp = BuildServices(dbFile);
        try
        {
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var um = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            await db.Database.EnsureCreatedAsync();

            var emp = new AppUser { UserName = "emp@example.com", Email = "emp@example.com", DisplayName = "Emp" };
            await um.CreateAsync(emp, "Emp-12345!");
            var principal = Principal(emp.Id);

            var (ctx, temp) = PageCtx(scope.ServiceProvider, principal);
            var create = new CreateModel(db, um, new TravelTracker.Web.Domain.TeamAccess(db)) { PageContext = ctx, TempData = temp };
            create.Input = new CreateModel.InputModel
            {
                Purpose = "Client onsite",
                StartDate = DateOnly.FromDateTime(DateTime.Today),
                EndDate = DateOnly.FromDateTime(DateTime.Today.AddDays(4))
            };
            var created = Assert.IsType<RedirectToPageResult>(await create.OnPostAsync());
            var tripId = Assert.IsType<int>(created.RouteValues!["id"]);

            var trip = await db.Trips.FindAsync(tripId);
            Assert.NotNull(trip);
            Assert.Equal(emp.Id, trip!.TravelerId);
            Assert.Equal(emp.Id, trip.CreatedById);
            Assert.Equal(TripStatus.Draft, trip.Status);

            await AddLeg(scope.ServiceProvider, db, principal, tripId, "London", "UK");
            await AddLeg(scope.ServiceProvider, db, principal, tripId, "Paris", "France");

            var legs = await db.Destinations.Where(d => d.TripId == tripId)
                .OrderBy(d => d.Sequence).ToListAsync();
            Assert.Equal(2, legs.Count);
            Assert.Equal(1, legs[0].Sequence);
            Assert.Equal(2, legs[1].Sequence);

            await Transition(scope.ServiceProvider, db, principal, tripId, TripStatus.Planned);
            Assert.Equal(TripStatus.Planned, (await Reload(db, tripId)).Status);

            await Transition(scope.ServiceProvider, db, principal, tripId, TripStatus.Completed);
            Assert.Equal(TripStatus.Completed, (await Reload(db, tripId)).Status);
        }
        finally
        {
            await sp.DisposeAsync();
            Cleanup(dbFile);
        }
    }

    [Fact]
    public async Task Invalid_Transition_Is_Rejected()
    {
        var dbFile = Path.Combine(Path.GetTempPath(), $"tt-trip-{Guid.NewGuid():N}.db");
        var sp = BuildServices(dbFile);
        try
        {
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var um = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            await db.Database.EnsureCreatedAsync();

            var emp = new AppUser { UserName = "e2@example.com", Email = "e2@example.com", DisplayName = "E2" };
            await um.CreateAsync(emp, "Emp-12345!");
            var trip = new Trip
            {
                TravelerId = emp.Id, Purpose = "X", Status = TripStatus.Draft,
                StartDate = DateOnly.FromDateTime(DateTime.Today),
                EndDate = DateOnly.FromDateTime(DateTime.Today)
            };
            db.Trips.Add(trip);
            await db.SaveChangesAsync();

            // Draft -> Completed is illegal; status must be unchanged.
            await Transition(scope.ServiceProvider, db, Principal(emp.Id), trip.Id, TripStatus.Completed);
            Assert.Equal(TripStatus.Draft, (await Reload(db, trip.Id)).Status);
        }
        finally
        {
            await sp.DisposeAsync();
            Cleanup(dbFile);
        }
    }

    [Fact]
    public async Task Employee_Cannot_Open_Another_Users_Trip()
    {
        var dbFile = Path.Combine(Path.GetTempPath(), $"tt-trip-{Guid.NewGuid():N}.db");
        var sp = BuildServices(dbFile);
        try
        {
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var um = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            await db.Database.EnsureCreatedAsync();

            var owner = new AppUser { UserName = "o@example.com", Email = "o@example.com", DisplayName = "Owner" };
            await um.CreateAsync(owner, "Emp-12345!");
            var trip = new Trip
            {
                TravelerId = owner.Id, Purpose = "Secret", Status = TripStatus.Draft,
                StartDate = DateOnly.FromDateTime(DateTime.Today),
                EndDate = DateOnly.FromDateTime(DateTime.Today)
            };
            db.Trips.Add(trip);
            await db.SaveChangesAsync();

            var (ctx, temp) = PageCtx(scope.ServiceProvider, Principal("intruder-id"));
            var details = new DetailsModel(db, new TravelTracker.Web.Domain.TeamAccess(db)) { PageContext = ctx, TempData = temp };
            var result = await details.OnGetAsync(trip.Id);
            Assert.IsType<NotFoundResult>(result);
        }
        finally
        {
            await sp.DisposeAsync();
            Cleanup(dbFile);
        }
    }

    private async Task AddLeg(IServiceProvider sp, AppDbContext db, ClaimsPrincipal user,
        int tripId, string city, string country)
    {
        var (ctx, temp) = PageCtx(sp, user);
        var details = new DetailsModel(db, new TravelTracker.Web.Domain.TeamAccess(db)) { PageContext = ctx, TempData = temp };
        details.NewLeg = new DetailsModel.LegInput
        {
            City = city, Country = country,
            ArriveDate = DateOnly.FromDateTime(DateTime.Today),
            DepartDate = DateOnly.FromDateTime(DateTime.Today.AddDays(1))
        };
        var result = await details.OnPostAddLegAsync(tripId);
        Assert.IsType<RedirectToPageResult>(result);
    }

    private async Task Transition(IServiceProvider sp, AppDbContext db, ClaimsPrincipal user,
        int tripId, TripStatus target)
    {
        var (ctx, temp) = PageCtx(sp, user);
        var details = new DetailsModel(db, new TravelTracker.Web.Domain.TeamAccess(db)) { PageContext = ctx, TempData = temp };
        var result = await details.OnPostStatusAsync(tripId, target);
        Assert.IsType<RedirectToPageResult>(result);
    }

    private static async Task<Trip> Reload(AppDbContext db, int id)
    {
        db.ChangeTracker.Clear(); // read persisted state, not a tracked copy
        return (await db.Trips.FindAsync(id))!;
    }

    private sealed class TestTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
}
