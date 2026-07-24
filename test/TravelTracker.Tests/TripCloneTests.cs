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

// Verifies IndexModel.OnPostCloneAsync deep-copies a trip and its legs into a
// new editable Draft owned by the same traveler.
public class TripCloneTests
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

    [Fact]
    public async Task Clone_Copies_Trip_And_Legs_As_New_Draft()
    {
        var dbFile = Path.Combine(Path.GetTempPath(), $"tt-clone-{Guid.NewGuid():N}.db");
        var sp = BuildServices(dbFile);
        try
        {
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var um = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            await db.Database.EnsureCreatedAsync();

            var emp = new AppUser { UserName = "e@x.com", Email = "e@x.com", DisplayName = "Emp" };
            await um.CreateAsync(emp, "Emp-12345!");

            var proj = new ProjectCode { Code = "PRJ-9", IsActive = true };
            db.ProjectCodes.Add(proj);
            await db.SaveChangesAsync();

            var trip = new Trip
            {
                TravelerId = emp.Id,
                Purpose = "Quarterly onsite",
                Status = TripStatus.Completed,
                Type = TripType.ClientVisit,
                ProjectCodeId = proj.Id,
                StartDate = new DateOnly(2026, 3, 1),
                EndDate = new DateOnly(2026, 3, 5),
                Destinations =
                {
                    new Destination { City = "Austin", State = "Texas", Country = "United States",
                        ArriveDate = new DateOnly(2026,3,1), DepartDate = new DateOnly(2026,3,5),
                        TransportMode = TransportMode.Flight, Sequence = 1 }
                }
            };
            db.Trips.Add(trip);
            await db.SaveChangesAsync();

            var (ctx, temp) = PageCtx(scope.ServiceProvider, Principal(emp.Id));
            var index = new IndexModel(db, um, new TravelTracker.Web.Domain.TeamAccess(db)) { PageContext = ctx, TempData = temp };
            var result = await index.OnPostCloneAsync(trip.Id);

            var redirect = Assert.IsType<RedirectToPageResult>(result);
            var newId = Assert.IsType<int>(redirect.RouteValues!["id"]);
            Assert.NotEqual(trip.Id, newId);

            var clone = await db.Trips.Include(t => t.Destinations)
                .FirstAsync(t => t.Id == newId);
            Assert.Equal(TripStatus.Draft, clone.Status);           // reset to Draft
            Assert.Equal(emp.Id, clone.TravelerId);
            Assert.Contains("(copy)", clone.Purpose);
            Assert.Equal(TripType.ClientVisit, clone.Type);          // metadata carried over
            Assert.Equal(proj.Id, clone.ProjectCodeId);              // FK link carried over
            Assert.Single(clone.Destinations);
            Assert.Equal("Austin", clone.Destinations[0].City);
            Assert.Equal(TransportMode.Flight, clone.Destinations[0].TransportMode);
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
