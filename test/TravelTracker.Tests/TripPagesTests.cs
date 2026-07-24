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
using TravelTracker.Web.Pages.Trips;
using Xunit;

namespace TravelTracker.Tests;

// Exercises the Trips page handlers that carry real logic (Edit, EditLeg,
// Calendar, WhoIsOut, Delete) through their public OnGet/OnPost surface, using
// the same direct-PageModel harness as TripWorkflowTests.
public class TripPagesTests
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

    private static TeamAccess Team(AppDbContext db) => new(db);

    private static async Task<AppUser> NewUser(UserManager<AppUser> um, string email, string name)
    {
        var u = new AppUser { UserName = email, Email = email, DisplayName = name, BaseLocation = "HQ" };
        await um.CreateAsync(u, "Emp-12345!");
        return u;
    }

    private static async Task<Trip> SeedTrip(AppDbContext db, string travelerId, bool withLeg)
    {
        var trip = new Trip
        {
            TravelerId = travelerId,
            CreatedById = travelerId,
            Purpose = "Original purpose",
            Status = TripStatus.Draft,
            StartDate = DateOnly.FromDateTime(DateTime.Today),
            EndDate = DateOnly.FromDateTime(DateTime.Today.AddDays(3))
        };
        if (withLeg)
        {
            trip.Destinations.Add(new Destination
            {
                Sequence = 1,
                City = "London",
                Country = "United Kingdom",
                ArriveDate = DateOnly.FromDateTime(DateTime.Today),
                DepartDate = DateOnly.FromDateTime(DateTime.Today.AddDays(2))
            });
        }
        db.Trips.Add(trip);
        await db.SaveChangesAsync();
        return trip;
    }

    // ---- Edit -------------------------------------------------------------

    [Fact]
    public async Task Edit_OnGet_Loads_Trip_For_Owner()
    {
        await WithScope(async (scope, db, um) =>
        {
            var emp = await NewUser(um, "edit-get@example.com", "Editor");
            var trip = await SeedTrip(db, emp.Id, withLeg: false);

            var (ctx, temp) = PageCtx(scope, Principal(emp.Id));
            var page = new EditModel(db, Team(db)) { PageContext = ctx, TempData = temp };
            var result = await page.OnGetAsync(trip.Id);

            Assert.IsType<PageResult>(result);
            Assert.Equal("Original purpose", page.Input.Purpose);
            Assert.Equal("Editor", page.TravelerName);
        });
    }

    [Fact]
    public async Task Edit_OnGet_ForeignTrip_Is_NotFound()
    {
        await WithScope(async (scope, db, um) =>
        {
            var owner = await NewUser(um, "edit-owner@example.com", "Owner");
            var trip = await SeedTrip(db, owner.Id, withLeg: false);

            var (ctx, temp) = PageCtx(scope, Principal("intruder"));
            var page = new EditModel(db, Team(db)) { PageContext = ctx, TempData = temp };
            Assert.IsType<NotFoundResult>(await page.OnGetAsync(trip.Id));
        });
    }

    [Fact]
    public async Task Edit_OnPost_Persists_Changes()
    {
        await WithScope(async (scope, db, um) =>
        {
            var emp = await NewUser(um, "edit-post@example.com", "Editor");
            var trip = await SeedTrip(db, emp.Id, withLeg: false);

            var proj = new TravelTracker.Web.Data.Entities.ProjectCode { Code = "PC-1", IsActive = true };
            db.ProjectCodes.Add(proj);
            await db.SaveChangesAsync();

            var (ctx, temp) = PageCtx(scope, Principal(emp.Id));
            var page = new EditModel(db, Team(db)) { PageContext = ctx, TempData = temp };
            page.Input = new EditModel.InputModel
            {
                Id = trip.Id,
                Purpose = "  Updated purpose  ",
                StartDate = DateOnly.FromDateTime(DateTime.Today),
                EndDate = DateOnly.FromDateTime(DateTime.Today.AddDays(5)),
                Type = TripType.Business,
                ProjectCodeId = proj.Id,
                Notes = "  "
            };

            var result = await page.OnPostAsync();
            var redirect = Assert.IsType<RedirectToPageResult>(result);
            Assert.Equal("Details", redirect.PageName);

            db.ChangeTracker.Clear();
            var saved = await db.Trips.FindAsync(trip.Id);
            Assert.Equal("Updated purpose", saved!.Purpose);
            Assert.Equal(proj.Id, saved.ProjectCodeId);
            Assert.Null(saved.Notes); // whitespace normalised to null
            Assert.Equal(TripType.Business, saved.Type);
        });
    }

    [Fact]
    public async Task Edit_OnPost_Rejects_Inverted_Dates()
    {
        await WithScope(async (scope, db, um) =>
        {
            var emp = await NewUser(um, "edit-bad@example.com", "Editor");
            var trip = await SeedTrip(db, emp.Id, withLeg: false);

            var (ctx, temp) = PageCtx(scope, Principal(emp.Id));
            var page = new EditModel(db, Team(db)) { PageContext = ctx, TempData = temp };
            page.Input = new EditModel.InputModel
            {
                Id = trip.Id,
                Purpose = "X",
                StartDate = DateOnly.FromDateTime(DateTime.Today.AddDays(5)),
                EndDate = DateOnly.FromDateTime(DateTime.Today)
            };

            Assert.IsType<PageResult>(await page.OnPostAsync());
            Assert.False(page.ModelState.IsValid);
        });
    }

    // ---- EditLeg ----------------------------------------------------------

    [Fact]
    public async Task EditLeg_OnGet_Loads_Leg()
    {
        await WithScope(async (scope, db, um) =>
        {
            var emp = await NewUser(um, "leg-get@example.com", "Traveler");
            var trip = await SeedTrip(db, emp.Id, withLeg: true);
            var legId = trip.Destinations.First().Id;

            var (ctx, temp) = PageCtx(scope, Principal(emp.Id));
            var page = new EditLegModel(db, Team(db)) { PageContext = ctx, TempData = temp };
            var result = await page.OnGetAsync(trip.Id, legId);

            Assert.IsType<PageResult>(result);
            Assert.Equal("London", page.Input.City);
            Assert.NotNull(page.Countries);
        });
    }

    [Fact]
    public async Task EditLeg_OnGet_MissingLeg_Is_NotFound()
    {
        await WithScope(async (scope, db, um) =>
        {
            var emp = await NewUser(um, "leg-miss@example.com", "Traveler");
            var trip = await SeedTrip(db, emp.Id, withLeg: true);

            var (ctx, temp) = PageCtx(scope, Principal(emp.Id));
            var page = new EditLegModel(db, Team(db)) { PageContext = ctx, TempData = temp };
            Assert.IsType<NotFoundResult>(await page.OnGetAsync(trip.Id, 999999));
        });
    }

    [Fact]
    public async Task EditLeg_OnPost_Persists_And_Reconciles_Window()
    {
        await WithScope(async (scope, db, um) =>
        {
            var emp = await NewUser(um, "leg-post@example.com", "Traveler");
            var trip = await SeedTrip(db, emp.Id, withLeg: true);
            var legId = trip.Destinations.First().Id;

            var (ctx, temp) = PageCtx(scope, Principal(emp.Id));
            var page = new EditLegModel(db, Team(db)) { PageContext = ctx, TempData = temp };
            page.Input = new EditLegModel.LegInput
            {
                Id = legId,
                City = "  Paris  ",
                Country = "France",
                ArriveDate = DateOnly.FromDateTime(DateTime.Today.AddDays(1)),
                DepartDate = DateOnly.FromDateTime(DateTime.Today.AddDays(6)),
                TransportMode = TransportMode.Flight
            };

            var redirect = Assert.IsType<RedirectToPageResult>(await page.OnPostAsync(trip.Id));
            Assert.Equal("Details", redirect.PageName);

            db.ChangeTracker.Clear();
            var savedLeg = await db.Destinations.FindAsync(legId);
            Assert.Equal("Paris", savedLeg!.City);
            var savedTrip = await db.Trips.FindAsync(trip.Id);
            // Window must stretch to cover the leg's later depart date.
            Assert.True(savedTrip!.EndDate >= savedLeg.DepartDate);
        });
    }

    [Fact]
    public async Task EditLeg_OnPost_Rejects_Inverted_Leg_Dates()
    {
        await WithScope(async (scope, db, um) =>
        {
            var emp = await NewUser(um, "leg-bad@example.com", "Traveler");
            var trip = await SeedTrip(db, emp.Id, withLeg: true);
            var legId = trip.Destinations.First().Id;

            var (ctx, temp) = PageCtx(scope, Principal(emp.Id));
            var page = new EditLegModel(db, Team(db)) { PageContext = ctx, TempData = temp };
            page.Input = new EditLegModel.LegInput
            {
                Id = legId,
                City = "Rome",
                Country = "Italy",
                ArriveDate = DateOnly.FromDateTime(DateTime.Today.AddDays(6)),
                DepartDate = DateOnly.FromDateTime(DateTime.Today.AddDays(1))
            };

            Assert.IsType<PageResult>(await page.OnPostAsync(trip.Id));
            Assert.False(page.ModelState.IsValid);
        });
    }

    // ---- Calendar ---------------------------------------------------------

    [Fact]
    public async Task Calendar_OnGet_Buckets_Trip_Into_Month()
    {
        await WithScope(async (scope, db, um) =>
        {
            var emp = await NewUser(um, "cal@example.com", "Traveler");
            var trip = await SeedTrip(db, emp.Id, withLeg: false);
            var today = DateOnly.FromDateTime(DateTime.Today);

            var (ctx, temp) = PageCtx(scope, Principal(emp.Id));
            var page = new CalendarModel(db, Team(db)) { PageContext = ctx, TempData = temp };
            await page.OnGetAsync(today.Year, today.Month);

            Assert.Equal(today.Year, page.Year);
            Assert.Equal(today.Month, page.Month);
            Assert.Contains(page.Trips, t => t.Id == trip.Id);
            Assert.Contains(page.TripsOn(today), t => t.Id == trip.Id);
        });
    }

    [Fact]
    public async Task Calendar_OnGet_Defaults_To_Current_Month()
    {
        await WithScope(async (scope, db, um) =>
        {
            var emp = await NewUser(um, "cal2@example.com", "Traveler");
            await SeedTrip(db, emp.Id, withLeg: false);

            var (ctx, temp) = PageCtx(scope, Principal(emp.Id));
            var page = new CalendarModel(db, Team(db)) { PageContext = ctx, TempData = temp };
            await page.OnGetAsync(null, null);

            Assert.Equal(DateTime.Today.Year, page.Year);
            Assert.True(page.DaysInMonth >= 28);
        });
    }

    // ---- WhoIsOut ---------------------------------------------------------

    [Fact]
    public async Task WhoIsOut_Admin_Sees_Active_Traveler_With_Location()
    {
        await WithScope(async (scope, db, um) =>
        {
            var emp = await NewUser(um, "out@example.com", "Away Person");
            await SeedTrip(db, emp.Id, withLeg: true);

            var (ctx, temp) = PageCtx(scope, Principal("admin-id", Roles.Admin));
            var page = new WhoIsOutModel(db, Team(db)) { PageContext = ctx, TempData = temp };
            await page.OnGetAsync("week");

            Assert.Equal("week", page.Range);
            var row = Assert.Single(page.Rows, r => r.Traveler == "Away Person");
            Assert.Contains("London", row.Where);
        });
    }

    // ---- Delete -----------------------------------------------------------

    [Fact]
    public async Task Delete_OnGet_Loads_Then_OnPost_Removes()
    {
        await WithScope(async (scope, db, um) =>
        {
            var emp = await NewUser(um, "del@example.com", "Traveler");
            var trip = await SeedTrip(db, emp.Id, withLeg: true);

            var (getCtx, getTemp) = PageCtx(scope, Principal(emp.Id));
            var getPage = new DeleteModel(db, Team(db)) { PageContext = getCtx, TempData = getTemp };
            Assert.IsType<PageResult>(await getPage.OnGetAsync(trip.Id));
            Assert.Equal(trip.Id, getPage.Trip.Id);

            var (postCtx, postTemp) = PageCtx(scope, Principal(emp.Id));
            var postPage = new DeleteModel(db, Team(db)) { PageContext = postCtx, TempData = postTemp };
            var redirect = Assert.IsType<RedirectToPageResult>(await postPage.OnPostAsync(trip.Id));
            Assert.Equal("Index", redirect.PageName);

            db.ChangeTracker.Clear();
            Assert.Null(await db.Trips.FindAsync(trip.Id));
        });
    }

    [Fact]
    public async Task Delete_OnPost_ForeignTrip_Is_NotFound()
    {
        await WithScope(async (scope, db, um) =>
        {
            var owner = await NewUser(um, "del-owner@example.com", "Owner");
            var trip = await SeedTrip(db, owner.Id, withLeg: false);

            var (ctx, temp) = PageCtx(scope, Principal("intruder"));
            var page = new DeleteModel(db, Team(db)) { PageContext = ctx, TempData = temp };
            Assert.IsType<NotFoundResult>(await page.OnPostAsync(trip.Id));

            db.ChangeTracker.Clear();
            Assert.NotNull(await db.Trips.FindAsync(trip.Id)); // untouched
        });
    }

    // Spins up an isolated database, hands the caller a scoped provider + db +
    // user manager, and always drops the database afterward.
    private static async Task WithScope(
        Func<IServiceProvider, AppDbContext, UserManager<AppUser>, Task> body)
    {
        var dbFile = Path.Combine(Path.GetTempPath(), $"tt-trippage-{Guid.NewGuid():N}.db");
        var sp = BuildServices(dbFile);
        try
        {
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var um = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            await db.Database.EnsureCreatedAsync();
            await body(scope.ServiceProvider, db, um);
        }
        finally
        {
            await sp.DisposeAsync();
            TestDatabase.Drop(TestDatabase.ConnectionStringFor(dbFile));
        }
    }

    private sealed class TestTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
}
