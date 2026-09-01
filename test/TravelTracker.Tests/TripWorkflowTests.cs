using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TravelTracker.Web.Data;
using TravelTracker.Web.Data.Entities;
using TravelTracker.Web.Pages.Trips;
using TravelTracker.Web.Services;
using Xunit;

namespace TravelTracker.Tests;

// Drives the real Create/Details page handlers end to end: create a trip, add
// multi-leg destinations, then walk the M6 approval lifecycle
// (Draft -> Submitted -> Approved -> Completed) through the actual handlers.
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

    // DetailsModel now takes an email sender + logger (M6 approval notifications).
    private static DetailsModel NewDetails(IServiceProvider sp, AppDbContext db, ClaimsPrincipal user)
    {
        var (ctx, temp) = PageCtx(sp, user);
        return new DetailsModel(db, new TravelTracker.Web.Domain.TeamAccess(db),
            new NoopEmail(), NullLogger<DetailsModel>.Instance)
        {
            PageContext = ctx,
            TempData = temp,
            Url = new StubUrlHelper(ctx)
        };
    }

    private static void Cleanup(string dbFile)
    {
        TestDatabase.Drop(TestDatabase.ConnectionStringFor(dbFile));
    }

    [Fact]
    public async Task Create_AddLegs_Submit_Approve_Complete_Flow()
    {
        var dbFile = Path.Combine(Path.GetTempPath(), $"tt-trip-{Guid.NewGuid():N}.db");
        var sp = BuildServices(dbFile);
        try
        {
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var um = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            await db.Database.EnsureCreatedAsync();

            var approver = new AppUser { UserName = "mgr@example.com", Email = "mgr@example.com", DisplayName = "Mgr" };
            await um.CreateAsync(approver, "Emp-12345!");
            var emp = new AppUser
            {
                UserName = "emp@example.com", Email = "emp@example.com", DisplayName = "Emp",
                ApproverId = approver.Id
            };
            await um.CreateAsync(emp, "Emp-12345!");
            var empUser = Principal(emp.Id);
            var approverUser = Principal(approver.Id);

            var (ctx, temp) = PageCtx(scope.ServiceProvider, empUser);
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
            Assert.Equal(TripStatus.Draft, trip!.Status);

            await AddLeg(scope.ServiceProvider, db, empUser, tripId, "London", "UK");
            await AddLeg(scope.ServiceProvider, db, empUser, tripId, "Paris", "France");

            var legs = await db.Destinations.Where(d => d.TripId == tripId)
                .OrderBy(d => d.Sequence).ToListAsync();
            Assert.Equal(2, legs.Count);

            // Traveler submits.
            var submit = NewDetails(scope.ServiceProvider, db, empUser);
            Assert.IsType<RedirectToPageResult>(await submit.OnPostSubmitAsync(tripId));
            Assert.Equal(TripStatus.Submitted, (await Reload(db, tripId)).Status);

            // Approver approves — an Approval row is recorded and status advances.
            var approve = NewDetails(scope.ServiceProvider, db, approverUser);
            Assert.IsType<RedirectToPageResult>(await approve.OnPostApproveAsync(tripId, "Looks good"));
            Assert.Equal(TripStatus.Approved, (await Reload(db, tripId)).Status);
            Assert.Equal(1, await db.Approvals.CountAsync(a => a.TripId == tripId && a.Decision == ApprovalDecision.Approved));

            // Traveler completes.
            var complete = NewDetails(scope.ServiceProvider, db, empUser);
            Assert.IsType<RedirectToPageResult>(await complete.OnPostStatusAsync(tripId, TripStatus.Completed));
            Assert.Equal(TripStatus.Completed, (await Reload(db, tripId)).Status);
        }
        finally
        {
            await sp.DisposeAsync();
            Cleanup(dbFile);
        }
    }

    [Fact]
    public async Task Reject_Requires_Comment_And_Records_History()
    {
        var dbFile = Path.Combine(Path.GetTempPath(), $"tt-trip-{Guid.NewGuid():N}.db");
        var sp = BuildServices(dbFile);
        try
        {
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var um = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            await db.Database.EnsureCreatedAsync();

            var approver = new AppUser { UserName = "a@example.com", Email = "a@example.com", DisplayName = "Appr" };
            await um.CreateAsync(approver, "Emp-12345!");
            var emp = new AppUser { UserName = "t@example.com", Email = "t@example.com", DisplayName = "Trav", ApproverId = approver.Id };
            await um.CreateAsync(emp, "Emp-12345!");
            var trip = new Trip
            {
                TravelerId = emp.Id, Purpose = "X", Status = TripStatus.Submitted,
                StartDate = DateOnly.FromDateTime(DateTime.Today),
                EndDate = DateOnly.FromDateTime(DateTime.Today)
            };
            db.Trips.Add(trip);
            await db.SaveChangesAsync();

            // Reject with no comment is refused; status stays Submitted.
            var noComment = NewDetails(scope.ServiceProvider, db, Principal(approver.Id));
            await noComment.OnPostRejectAsync(trip.Id, "   ");
            Assert.Equal(TripStatus.Submitted, (await Reload(db, trip.Id)).Status);
            Assert.Equal(0, await db.Approvals.CountAsync(a => a.TripId == trip.Id));

            // Reject with a comment records history and moves to Rejected.
            var reject = NewDetails(scope.ServiceProvider, db, Principal(approver.Id));
            await reject.OnPostRejectAsync(trip.Id, "Missing business purpose");
            Assert.Equal(TripStatus.Rejected, (await Reload(db, trip.Id)).Status);
            Assert.Equal(1, await db.Approvals.CountAsync(a => a.TripId == trip.Id && a.Decision == ApprovalDecision.Rejected));
        }
        finally
        {
            await sp.DisposeAsync();
            Cleanup(dbFile);
        }
    }

    [Fact]
    public async Task NonApprover_Cannot_Approve()
    {
        var dbFile = Path.Combine(Path.GetTempPath(), $"tt-trip-{Guid.NewGuid():N}.db");
        var sp = BuildServices(dbFile);
        try
        {
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var um = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            await db.Database.EnsureCreatedAsync();

            var approver = new AppUser { UserName = "ap@example.com", Email = "ap@example.com", DisplayName = "Appr" };
            await um.CreateAsync(approver, "Emp-12345!");
            var emp = new AppUser { UserName = "tr@example.com", Email = "tr@example.com", DisplayName = "Trav", ApproverId = approver.Id };
            await um.CreateAsync(emp, "Emp-12345!");
            var intruder = new AppUser { UserName = "in@example.com", Email = "in@example.com", DisplayName = "In" };
            await um.CreateAsync(intruder, "Emp-12345!");
            var trip = new Trip
            {
                TravelerId = emp.Id, Purpose = "X", Status = TripStatus.Submitted,
                StartDate = DateOnly.FromDateTime(DateTime.Today),
                EndDate = DateOnly.FromDateTime(DateTime.Today)
            };
            db.Trips.Add(trip);
            await db.SaveChangesAsync();

            // A random user who isn't the approver can't even load the trip -> NotFound.
            var intruderDetails = NewDetails(scope.ServiceProvider, db, Principal(intruder.Id));
            Assert.IsType<NotFoundResult>(await intruderDetails.OnPostApproveAsync(trip.Id, "ok"));
            Assert.Equal(TripStatus.Submitted, (await Reload(db, trip.Id)).Status);
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
            var details = NewDetails(scope.ServiceProvider, db, Principal(emp.Id));
            await details.OnPostStatusAsync(trip.Id, TripStatus.Completed);
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

            var details = NewDetails(scope.ServiceProvider, db, Principal("intruder-id"));
            var result = await details.OnGetAsync(trip.Id);
            Assert.IsType<NotFoundResult>(result);
        }
        finally
        {
            await sp.DisposeAsync();
            Cleanup(dbFile);
        }
    }

    [Fact]
    public async Task Approved_Trip_Itinerary_Is_Locked_Until_Revised()
    {
        var dbFile = Path.Combine(Path.GetTempPath(), $"tt-trip-{Guid.NewGuid():N}.db");
        var sp = BuildServices(dbFile);
        try
        {
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var um = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            await db.Database.EnsureCreatedAsync();

            var approver = new AppUser { UserName = "mgr@example.com", Email = "mgr@example.com", DisplayName = "Mgr" };
            await um.CreateAsync(approver, "Emp-12345!");
            var emp = new AppUser
            {
                UserName = "emp@example.com", Email = "emp@example.com", DisplayName = "Emp",
                ApproverId = approver.Id
            };
            await um.CreateAsync(emp, "Emp-12345!");
            var empUser = Principal(emp.Id);
            var approverUser = Principal(approver.Id);

            var trip = new Trip
            {
                TravelerId = emp.Id, Purpose = "Onsite", Status = TripStatus.Draft,
                StartDate = DateOnly.FromDateTime(DateTime.Today),
                EndDate = DateOnly.FromDateTime(DateTime.Today.AddDays(2))
            };
            db.Trips.Add(trip);
            await db.SaveChangesAsync();
            var tripId = trip.Id;

            await AddLeg(scope.ServiceProvider, db, empUser, tripId, "London", "UK");

            // Submit + approve.
            Assert.IsType<RedirectToPageResult>(await NewDetails(scope.ServiceProvider, db, empUser).OnPostSubmitAsync(tripId));
            Assert.IsType<RedirectToPageResult>(await NewDetails(scope.ServiceProvider, db, approverUser).OnPostApproveAsync(tripId, "ok"));
            Assert.Equal(TripStatus.Approved, (await Reload(db, tripId)).Status);

            // Adding a leg while Approved is refused: no new leg persists.
            var addWhileLocked = NewDetails(scope.ServiceProvider, db, empUser);
            addWhileLocked.NewLeg = new DetailsModel.LegInput
            {
                City = "Paris", Country = "France",
                ArriveDate = DateOnly.FromDateTime(DateTime.Today),
                DepartDate = DateOnly.FromDateTime(DateTime.Today.AddDays(1))
            };
            Assert.IsType<RedirectToPageResult>(await addWhileLocked.OnPostAddLegAsync(tripId));
            Assert.Equal(1, await db.Destinations.CountAsync(d => d.TripId == tripId));

            // Removing a leg while Approved is refused too.
            var existingLeg = await db.Destinations.FirstAsync(d => d.TripId == tripId);
            Assert.IsType<RedirectToPageResult>(
                await NewDetails(scope.ServiceProvider, db, empUser).OnPostRemoveLegAsync(tripId, existingLeg.Id));
            Assert.Equal(1, await db.Destinations.CountAsync(d => d.TripId == tripId));

            // Revise: Approved -> Draft records a Reopened audit row and unlocks editing.
            Assert.IsType<RedirectToPageResult>(
                await NewDetails(scope.ServiceProvider, db, empUser).OnPostStatusAsync(tripId, TripStatus.Draft));
            Assert.Equal(TripStatus.Draft, (await Reload(db, tripId)).Status);
            Assert.Equal(1, await db.Approvals.CountAsync(a => a.TripId == tripId && a.Decision == ApprovalDecision.Reopened));

            // Now the itinerary is editable again.
            await AddLeg(scope.ServiceProvider, db, empUser, tripId, "Paris", "France");
            Assert.Equal(2, await db.Destinations.CountAsync(d => d.TripId == tripId));
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
        var details = NewDetails(sp, db, user);
        details.NewLeg = new DetailsModel.LegInput
        {
            City = city, Country = country,
            ArriveDate = DateOnly.FromDateTime(DateTime.Today),
            DepartDate = DateOnly.FromDateTime(DateTime.Today.AddDays(1))
        };
        var result = await details.OnPostAddLegAsync(tripId);
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

    // Handlers that email absolute links call Url.Page, which needs an IUrlHelper.
    // Unit tests build the model directly, so supply a stub; the URL value is not asserted.
    private sealed class StubUrlHelper : IUrlHelper
    {
        public StubUrlHelper(ActionContext actionContext) => ActionContext = actionContext;
        public ActionContext ActionContext { get; }
        public string? Action(UrlActionContext actionContext) => "/";
        public string? Content(string? contentPath) => contentPath;
        public bool IsLocalUrl(string? url) => true;
        public string? Link(string? routeName, object? values) => "http://localhost/";
        public string? RouteUrl(UrlRouteContext routeContext) => "http://localhost/Trips/Details/1";
    }

    // Approval tests assert on persisted rows, not on email, so a no-op is enough.
    private sealed class NoopEmail : IEmailSender
    {
        public Task SendAsync(string recipient, string subject, string htmlBody) => Task.CompletedTask;
    }
}
