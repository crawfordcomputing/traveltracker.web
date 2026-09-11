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

// Integration coverage for ADR-0004: codes are assigned centrally on save (so
// Create, Clone, and the seeders all get one), the unique index holds, the
// /Trips/Code/{code} route honors TripAccess, and the Trips list search matches
// full codes and the random suffix. Same direct-PageModel harness as TripCloneTests.
public class TripCodeAssignmentTests
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

    private static ClaimsPrincipal Principal(string userId) =>
        new(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, userId) }, "Test"));

    private static (PageContext ctx, TempDataDictionary temp) PageCtx(IServiceProvider sp, ClaimsPrincipal user)
    {
        var http = new DefaultHttpContext { RequestServices = sp, User = user };
        var ac = new ActionContext(http, new RouteData(), new PageActionDescriptor(), new ModelStateDictionary());
        return (new PageContext(ac), new TempDataDictionary(http, new TestTempDataProvider()));
    }

    private static Trip NewTrip(string travelerId, string purpose, DateTimeOffset? createdAt = null) => new()
    {
        TravelerId = travelerId,
        Purpose = purpose,
        Status = TripStatus.Draft,
        StartDate = new DateOnly(2027, 1, 10),
        EndDate = new DateOnly(2027, 1, 12),
        CreatedAt = createdAt ?? DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task Save_Assigns_Code_With_CreatedAt_Year_And_Clone_Gets_A_New_One()
    {
        var dbFile = Path.Combine(Path.GetTempPath(), $"tt-code-{Guid.NewGuid():N}.db");
        var sp = BuildServices(dbFile);
        try
        {
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var um = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            await db.Database.EnsureCreatedAsync();

            var emp = new AppUser { UserName = "e@x.com", Email = "e@x.com", DisplayName = "Emp" };
            await um.CreateAsync(emp, "Emp-12345!");

            // Created in Dec 2026 for a Jan 2027 trip: the code year must follow
            // CreatedAt, not StartDate.
            var trip = NewTrip(emp.Id, "Kickoff", new DateTimeOffset(2026, 12, 20, 0, 0, 0, TimeSpan.Zero));
            var batchMate = NewTrip(emp.Id, "Second in same save");
            db.Trips.AddRange(trip, batchMate);
            await db.SaveChangesAsync();

            Assert.StartsWith("TT-2026-", trip.Code);
            Assert.True(TripCode.TryParse(trip.Code, out var canonical));
            Assert.Equal(canonical, trip.Code);                      // stored canonical
            Assert.NotEqual(trip.Code, batchMate.Code);              // same batch, distinct codes

            // A later save must never touch the code.
            trip.Purpose = "Kickoff (renamed)";
            await db.SaveChangesAsync();
            Assert.Equal(canonical, (await db.Trips.AsNoTracking().FirstAsync(t => t.Id == trip.Id)).Code);

            var (ctx, temp) = PageCtx(scope.ServiceProvider, Principal(emp.Id));
            var index = new IndexModel(db, um, new TeamAccess(db)) { PageContext = ctx, TempData = temp };
            var redirect = Assert.IsType<RedirectToPageResult>(await index.OnPostCloneAsync(trip.Id));
            var cloneId = Assert.IsType<int>(redirect.RouteValues!["id"]);

            var clone = await db.Trips.AsNoTracking().FirstAsync(t => t.Id == cloneId);
            Assert.Matches(@"^TT-\d{4}-[0-9A-HJKMNP-TV-Z]{3}-[0-9A-HJKMNP-TV-Z]{3}$", clone.Code);
            Assert.NotEqual(trip.Code, clone.Code);
        }
        finally
        {
            TestDatabase.Drop(TestDatabase.ConnectionStringFor(dbFile));
        }
    }

    [Fact]
    public async Task Unique_Index_Rejects_A_Forced_Duplicate()
    {
        var dbFile = Path.Combine(Path.GetTempPath(), $"tt-code-{Guid.NewGuid():N}.db");
        var sp = BuildServices(dbFile);
        try
        {
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var um = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            await db.Database.EnsureCreatedAsync();

            var emp = new AppUser { UserName = "e@x.com", Email = "e@x.com", DisplayName = "Emp" };
            await um.CreateAsync(emp, "Emp-12345!");

            var first = NewTrip(emp.Id, "First");
            db.Trips.Add(first);
            await db.SaveChangesAsync();

            // A pre-set code bypasses assignment (only empty codes are generated),
            // which is exactly the concurrent-draw race the index backstops.
            var dupe = NewTrip(emp.Id, "Dupe");
            dupe.Code = first.Code;
            db.Trips.Add(dupe);
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
        finally
        {
            TestDatabase.Drop(TestDatabase.ConnectionStringFor(dbFile));
        }
    }

    [Fact]
    public async Task Code_Route_Redirects_Own_Trip_And_Hides_Everything_Else()
    {
        var dbFile = Path.Combine(Path.GetTempPath(), $"tt-code-{Guid.NewGuid():N}.db");
        var sp = BuildServices(dbFile);
        try
        {
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var um = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            await db.Database.EnsureCreatedAsync();

            var owner = new AppUser { UserName = "o@x.com", Email = "o@x.com", DisplayName = "Owner" };
            var other = new AppUser { UserName = "s@x.com", Email = "s@x.com", DisplayName = "Stranger" };
            await um.CreateAsync(owner, "Own-12345!");
            await um.CreateAsync(other, "Str-12345!");

            var trip = NewTrip(owner.Id, "Mine");
            db.Trips.Add(trip);
            await db.SaveChangesAsync();

            // Owner: canonical code and a sloppy, lower-case, space-separated variant.
            var (octx, otemp) = PageCtx(scope.ServiceProvider, Principal(owner.Id));
            var page = new CodeModel(db, new TeamAccess(db)) { PageContext = octx, TempData = otemp };

            var r1 = Assert.IsType<RedirectToPageResult>(await page.OnGetAsync(trip.Code));
            Assert.Equal(trip.Id, r1.RouteValues!["id"]);

            var sloppy = trip.Code.Replace("-", " ").ToLowerInvariant();
            var r2 = Assert.IsType<RedirectToPageResult>(await page.OnGetAsync(sloppy));
            Assert.Equal(trip.Id, r2.RouteValues!["id"]);

            Assert.IsType<NotFoundResult>(await page.OnGetAsync("TT-2026-ZZZ-ZZZ")); // unknown
            Assert.IsType<NotFoundResult>(await page.OnGetAsync("not a code"));       // malformed

            // A plain employee with no reach to the owner gets the same 404.
            var (sctx, stemp) = PageCtx(scope.ServiceProvider, Principal(other.Id));
            var stranger = new CodeModel(db, new TeamAccess(db)) { PageContext = sctx, TempData = stemp };
            Assert.IsType<NotFoundResult>(await stranger.OnGetAsync(trip.Code));
        }
        finally
        {
            TestDatabase.Drop(TestDatabase.ConnectionStringFor(dbFile));
        }
    }

    [Fact]
    public async Task List_Search_Matches_Full_Code_And_Suffix()
    {
        var dbFile = Path.Combine(Path.GetTempPath(), $"tt-code-{Guid.NewGuid():N}.db");
        var sp = BuildServices(dbFile);
        try
        {
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var um = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            await db.Database.EnsureCreatedAsync();

            var emp = new AppUser { UserName = "e@x.com", Email = "e@x.com", DisplayName = "Emp" };
            await um.CreateAsync(emp, "Emp-12345!");

            var target = NewTrip(emp.Id, "Target");
            var noise = NewTrip(emp.Id, "Noise");
            db.Trips.AddRange(target, noise);
            await db.SaveChangesAsync();

            var (ctx, temp) = PageCtx(scope.ServiceProvider, Principal(emp.Id));

            async Task<List<int>> SearchAsync(string term)
            {
                var index = new IndexModel(db, um, new TeamAccess(db))
                {
                    PageContext = ctx, TempData = temp, When = "all", Search = term
                };
                await index.OnGetAsync();
                return index.Trips.Select(t => t.Id).ToList();
            }

            Assert.Equal(new[] { target.Id }, await SearchAsync(target.Code));
            Assert.Equal(new[] { target.Id }, await SearchAsync(target.Code.ToLowerInvariant().Replace("-", "")));

            var suffix = target.Code[^7..].Replace("-", "");      // 6 random chars
            Assert.Equal(new[] { target.Id }, await SearchAsync(suffix));
            Assert.Equal(new[] { target.Id }, await SearchAsync(suffix.ToLowerInvariant()));

            // Purpose search still works alongside the code match.
            Assert.Equal(new[] { noise.Id }, await SearchAsync("Noise"));
        }
        finally
        {
            TestDatabase.Drop(TestDatabase.ConnectionStringFor(dbFile));
        }
    }

    private sealed class TestTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext c) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext c, IDictionary<string, object> v) { }
    }
}
