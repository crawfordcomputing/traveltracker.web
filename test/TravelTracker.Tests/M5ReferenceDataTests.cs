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

// M5: cost centers / project codes as managed reference data feeding the trip and
// profile dropdowns. Covers the select-list rules, the management pages, and the
// Trip/Create default-cost-center prefill.
public class M5ReferenceDataTests
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
        return (new PageContext(ac), new TempDataDictionary(http, new NoopTempDataProvider()));
    }

    private static async Task WithScope(Func<IServiceProvider, AppDbContext, UserManager<AppUser>, Task> body)
    {
        var dbFile = Path.Combine(Path.GetTempPath(), $"tt-m5ref-{Guid.NewGuid():N}.db");
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

    // ---- Select list rules ------------------------------------------------

    [Fact]
    public async Task CostCenterSelectList_Active_Only_Ordered_By_Code()
    {
        await WithScope(async (_, db, __) =>
        {
            db.CostCenters.AddRange(
                new CostCenter { Code = "B-2", IsActive = true },
                new CostCenter { Code = "A-1", IsActive = true },
                new CostCenter { Code = "Z-9", IsActive = false });
            await db.SaveChangesAsync();

            var list = await db.CostCenterSelectListAsync();
            var codes = list.Select(i => i.Text).ToList();

            Assert.Equal(new[] { "A-1", "B-2" }, codes); // inactive dropped, ordered by code
        });
    }

    [Fact]
    public async Task CostCenterSelectList_Includes_Selected_Even_When_Inactive()
    {
        await WithScope(async (_, db, __) =>
        {
            var retired = new CostCenter { Code = "OLD-1", IsActive = false };
            db.CostCenters.Add(retired);
            db.CostCenters.Add(new CostCenter { Code = "NEW-1", IsActive = true });
            await db.SaveChangesAsync();

            var list = await db.CostCenterSelectListAsync(retired.Id);
            var values = list.Select(i => i.Value).ToList();

            Assert.Contains(retired.Id.ToString(), values); // selected retired code stays selectable
            Assert.Equal(retired.Id.ToString(), list.SelectedValue?.ToString());
        });
    }

    [Fact]
    public async Task CostCenterSelectList_Label_Combines_Code_And_Name()
    {
        await WithScope(async (_, db, __) =>
        {
            db.CostCenters.Add(new CostCenter { Code = "ENG-100", Name = "Engineering", IsActive = true });
            db.CostCenters.Add(new CostCenter { Code = "OPS-300", Name = null, IsActive = true });
            await db.SaveChangesAsync();

            var list = await db.CostCenterSelectListAsync();
            var labels = list.Select(i => i.Text).ToList();

            Assert.Contains("ENG-100 — Engineering", labels);
            Assert.Contains("OPS-300", labels); // name-less falls back to bare code
        });
    }

    // ---- Management pages -------------------------------------------------

    [Fact]
    public async Task CostCenters_Create_Then_Rejects_Duplicate()
    {
        await WithScope(async (sp, db, __) =>
        {
            var (ctx, temp) = PageCtx(sp, Principal("finance", "Finance"));
            var page = new TravelTracker.Web.Pages.Reference.CostCenters.IndexModel(db)
            { PageContext = ctx, TempData = temp, NewCode = " ENG-100 ", NewName = " Platform " };

            Assert.IsType<RedirectToPageResult>(await page.OnPostCreateAsync());
            var saved = await db.CostCenters.SingleAsync();
            Assert.Equal("ENG-100", saved.Code); // trimmed
            Assert.Equal("Platform", saved.Name);
            Assert.True(saved.IsActive);

            var dup = new TravelTracker.Web.Pages.Reference.CostCenters.IndexModel(db)
            { PageContext = ctx, TempData = temp, NewCode = "ENG-100" };
            Assert.IsType<PageResult>(await dup.OnPostCreateAsync());
            Assert.False(dup.ModelState.IsValid);
            Assert.Equal(1, await db.CostCenters.CountAsync()); // no second row
        });
    }

    [Fact]
    public async Task CostCenters_Toggle_Flips_Active()
    {
        await WithScope(async (sp, db, __) =>
        {
            var cc = new CostCenter { Code = "T-1", IsActive = true };
            db.CostCenters.Add(cc);
            await db.SaveChangesAsync();

            var (ctx, temp) = PageCtx(sp, Principal("admin", "Admin"));
            var page = new TravelTracker.Web.Pages.Reference.CostCenters.IndexModel(db)
            { PageContext = ctx, TempData = temp };

            await page.OnPostToggleAsync(cc.Id);
            db.ChangeTracker.Clear();
            Assert.False((await db.CostCenters.FindAsync(cc.Id))!.IsActive);
        });
    }

    [Fact]
    public async Task CostCenters_Delete_Nulls_References()
    {
        await WithScope(async (sp, db, um) =>
        {
            var cc = new CostCenter { Code = "D-1", IsActive = true };
            db.CostCenters.Add(cc);
            await db.SaveChangesAsync();

            var user = new AppUser { UserName = "u@x.com", Email = "u@x.com", DisplayName = "U", DefaultCostCenterId = cc.Id };
            await um.CreateAsync(user, "Emp-12345!");
            db.Trips.Add(new Trip
            {
                TravelerId = user.Id, CreatedById = user.Id, Purpose = "P", Status = TripStatus.Draft,
                StartDate = DateOnly.FromDateTime(DateTime.Today), EndDate = DateOnly.FromDateTime(DateTime.Today),
                CostCenterId = cc.Id
            });
            await db.SaveChangesAsync();

            var (ctx, temp) = PageCtx(sp, Principal("admin", "Admin"));
            var page = new TravelTracker.Web.Pages.Reference.CostCenters.IndexModel(db)
            { PageContext = ctx, TempData = temp };
            await page.OnPostDeleteAsync(cc.Id);

            db.ChangeTracker.Clear();
            Assert.Null(await db.CostCenters.FindAsync(cc.Id));
            Assert.Null((await db.Trips.FirstAsync()).CostCenterId);      // link cleared, trip kept
            Assert.Null((await db.Users.FirstAsync()).DefaultCostCenterId);
        });
    }

    [Fact]
    public async Task ProjectCodes_Create_Then_Rejects_Duplicate()
    {
        await WithScope(async (sp, db, __) =>
        {
            var (ctx, temp) = PageCtx(sp, Principal("manager", "Manager"));
            var page = new TravelTracker.Web.Pages.Reference.ProjectCodes.IndexModel(db)
            { PageContext = ctx, TempData = temp, NewCode = "PRJ-1" };
            Assert.IsType<RedirectToPageResult>(await page.OnPostCreateAsync());

            var dup = new TravelTracker.Web.Pages.Reference.ProjectCodes.IndexModel(db)
            { PageContext = ctx, TempData = temp, NewCode = "PRJ-1" };
            Assert.IsType<PageResult>(await dup.OnPostCreateAsync());
            Assert.Equal(1, await db.ProjectCodes.CountAsync());
        });
    }

    // ---- Trip create prefill ---------------------------------------------

    [Fact]
    public async Task TripCreate_Prefills_Default_Cost_Center_From_Profile()
    {
        await WithScope(async (sp, db, um) =>
        {
            var cc = new CostCenter { Code = "ENG-100", IsActive = true };
            db.CostCenters.Add(cc);
            await db.SaveChangesAsync();

            var user = new AppUser { UserName = "t@x.com", Email = "t@x.com", DisplayName = "T", DefaultCostCenterId = cc.Id };
            await um.CreateAsync(user, "Emp-12345!");

            var (ctx, temp) = PageCtx(sp, Principal(user.Id));
            var page = new TravelTracker.Web.Pages.Trips.CreateModel(db, um, new TeamAccess(db))
            { PageContext = ctx, TempData = temp };
            await page.OnGetAsync();

            Assert.Equal(cc.Id, page.Input.CostCenterId); // default flows into the new-trip form
        });
    }

    private sealed class NoopTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
}
