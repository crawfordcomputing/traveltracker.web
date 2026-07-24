using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TravelTracker.Web.Data;
using TravelTracker.Web.Data.Entities;
using TravelTracker.Web.Pages.Admin.Users;
using TravelTracker.Web.Services;
using Xunit;

namespace TravelTracker.Tests;

// Drives the real Admin/Users EditModel handlers: load, profile+role change, and
// the last-active-admin demotion guard.
public class AdminUserEditTests
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

    private static readonly ISensitiveFieldProtector Protector =
        new SensitiveFieldProtector(DataProtectionProvider.Create("TravelTracker.Tests"));

    private static EditModel NewPage(IServiceProvider sp, AppDbContext db, UserManager<AppUser> um)
    {
        var http = new DefaultHttpContext { RequestServices = sp };
        var ac = new ActionContext(http, new RouteData(), new PageActionDescriptor(), new ModelStateDictionary());
        return new EditModel(db, um, Protector)
        {
            PageContext = new PageContext(ac),
            TempData = new TempDataDictionary(http, new TestTempDataProvider())
        };
    }

    private static async Task<AppUser> NewUserInRole(
        UserManager<AppUser> um, RoleManager<IdentityRole> rm, string email, string name, string role)
    {
        var u = new AppUser { UserName = email, Email = email, DisplayName = name };
        await um.CreateAsync(u, "Emp-12345!");
        await um.AddToRoleAsync(u, role);
        return u;
    }

    [Fact]
    public async Task OnGet_Loads_User_And_Current_Role()
    {
        await WithScope(async (sp, db, um, rm) =>
        {
            var user = await NewUserInRole(um, rm, "e-get@example.com", "Ed Get", Roles.Employee);

            var page = NewPage(sp, db, um);
            var result = await page.OnGetAsync(user.Id);

            Assert.IsType<PageResult>(result);
            Assert.Equal("Ed Get", page.Input.DisplayName);
            Assert.Equal(Roles.Employee, page.Input.Role);
            Assert.NotNull(page.RoleOptions);
        });
    }

    [Fact]
    public async Task OnGet_Unknown_User_Is_NotFound()
    {
        await WithScope(async (sp, db, um, rm) =>
        {
            var page = NewPage(sp, db, um);
            Assert.IsType<NotFoundResult>(await page.OnGetAsync("no-such-id"));
        });
    }

    [Fact]
    public async Task OnPost_Updates_Profile_And_Changes_Role()
    {
        await WithScope(async (sp, db, um, rm) =>
        {
            var user = await NewUserInRole(um, rm, "e-post@example.com", "Old Name", Roles.Employee);

            var page = NewPage(sp, db, um);
            page.Input = new EditModel.InputModel
            {
                Id = user.Id,
                DisplayName = "New Name",
                Role = Roles.Manager,
                BaseLocation = "  Berlin  ",
                TimeZoneId = "  "
            };

            var result = await page.OnPostAsync();
            var redirect = Assert.IsType<RedirectToPageResult>(result);
            Assert.Equal("/Admin/Users/Index", redirect.PageName);

            var reloaded = await um.FindByIdAsync(user.Id);
            Assert.Equal("New Name", reloaded!.DisplayName);
            Assert.Equal("Berlin", reloaded.BaseLocation);
            Assert.Null(reloaded.TimeZoneId); // whitespace normalised to null
            Assert.True(await um.IsInRoleAsync(reloaded, Roles.Manager));
            Assert.False(await um.IsInRoleAsync(reloaded, Roles.Employee));
        });
    }

    [Fact]
    public async Task OnPost_Rejects_Unknown_Role()
    {
        await WithScope(async (sp, db, um, rm) =>
        {
            var user = await NewUserInRole(um, rm, "e-badrole@example.com", "Bad Role", Roles.Employee);

            var page = NewPage(sp, db, um);
            page.Input = new EditModel.InputModel
            {
                Id = user.Id, DisplayName = "Bad Role", Role = "Wizard"
            };

            Assert.IsType<PageResult>(await page.OnPostAsync());
            Assert.False(page.ModelState.IsValid);
            Assert.True(await um.IsInRoleAsync((await um.FindByIdAsync(user.Id))!, Roles.Employee));
        });
    }

    [Fact]
    public async Task OnPost_Blocks_Demoting_Last_Active_Admin()
    {
        await WithScope(async (sp, db, um, rm) =>
        {
            var admin = await NewUserInRole(um, rm, "sole-admin@example.com", "Sole Admin", Roles.Admin);

            var page = NewPage(sp, db, um);
            page.Input = new EditModel.InputModel
            {
                Id = admin.Id, DisplayName = "Sole Admin", Role = Roles.Employee
            };

            Assert.IsType<PageResult>(await page.OnPostAsync());
            Assert.False(page.ModelState.IsValid);
            Assert.True(await um.IsInRoleAsync((await um.FindByIdAsync(admin.Id))!, Roles.Admin));
        });
    }

    [Fact]
    public async Task OnPost_Allows_Demoting_Admin_When_Another_Admin_Exists()
    {
        await WithScope(async (sp, db, um, rm) =>
        {
            var keep = await NewUserInRole(um, rm, "keep-admin@example.com", "Keep Admin", Roles.Admin);
            var demote = await NewUserInRole(um, rm, "demote-admin@example.com", "Demote Admin", Roles.Admin);

            var page = NewPage(sp, db, um);
            page.Input = new EditModel.InputModel
            {
                Id = demote.Id, DisplayName = "Demote Admin", Role = Roles.Manager
            };

            Assert.IsType<RedirectToPageResult>(await page.OnPostAsync());
            Assert.True(await um.IsInRoleAsync((await um.FindByIdAsync(demote.Id))!, Roles.Manager));
            Assert.True(await um.IsInRoleAsync((await um.FindByIdAsync(keep.Id))!, Roles.Admin));
        });
    }

    [Fact]
    public async Task OnPost_Assigns_Approver()
    {
        await WithScope(async (sp, db, um, rm) =>
        {
            var approver = await NewUserInRole(um, rm, "boss@example.com", "Boss", Roles.Manager);
            var user = await NewUserInRole(um, rm, "rep@example.com", "Rep", Roles.Employee);

            var page = NewPage(sp, db, um);
            page.Input = new EditModel.InputModel
            {
                Id = user.Id, DisplayName = "Rep", Role = Roles.Employee, ApproverId = approver.Id
            };

            Assert.IsType<RedirectToPageResult>(await page.OnPostAsync());
            Assert.Equal(approver.Id, (await um.FindByIdAsync(user.Id))!.ApproverId);
        });
    }

    [Fact]
    public async Task OnPost_Rejects_Self_As_Approver()
    {
        await WithScope(async (sp, db, um, rm) =>
        {
            var user = await NewUserInRole(um, rm, "self@example.com", "Self", Roles.Employee);

            var page = NewPage(sp, db, um);
            page.Input = new EditModel.InputModel
            {
                Id = user.Id, DisplayName = "Self", Role = Roles.Employee, ApproverId = user.Id
            };

            Assert.IsType<PageResult>(await page.OnPostAsync());
            Assert.False(page.ModelState.IsValid);
            Assert.Null((await um.FindByIdAsync(user.Id))!.ApproverId);
        });
    }

    [Fact]
    public async Task OnPost_Rejects_Cyclic_Approver()
    {
        await WithScope(async (sp, db, um, rm) =>
        {
            // a -> b already (a's approver is b). Making b's approver a closes the loop.
            var a = await NewUserInRole(um, rm, "a@example.com", "A", Roles.Employee);
            var b = await NewUserInRole(um, rm, "b@example.com", "B", Roles.Manager);
            a.ApproverId = b.Id;
            await um.UpdateAsync(a);

            var page = NewPage(sp, db, um);
            page.Input = new EditModel.InputModel
            {
                Id = b.Id, DisplayName = "B", Role = Roles.Manager, ApproverId = a.Id
            };

            Assert.IsType<PageResult>(await page.OnPostAsync());
            Assert.False(page.ModelState.IsValid);
            Assert.Null((await um.FindByIdAsync(b.Id))!.ApproverId);
        });
    }

    [Fact]
    public async Task OnPost_Clears_Approver_When_Blank()
    {
        await WithScope(async (sp, db, um, rm) =>
        {
            var approver = await NewUserInRole(um, rm, "boss2@example.com", "Boss2", Roles.Manager);
            var user = await NewUserInRole(um, rm, "rep2@example.com", "Rep2", Roles.Employee);
            user.ApproverId = approver.Id;
            await um.UpdateAsync(user);

            var page = NewPage(sp, db, um);
            page.Input = new EditModel.InputModel
            {
                Id = user.Id, DisplayName = "Rep2", Role = Roles.Employee, ApproverId = "  "
            };

            Assert.IsType<RedirectToPageResult>(await page.OnPostAsync());
            Assert.Null((await um.FindByIdAsync(user.Id))!.ApproverId);
        });
    }

    private static async Task WithScope(
        Func<IServiceProvider, AppDbContext, UserManager<AppUser>, RoleManager<IdentityRole>, Task> body)
    {
        var dbFile = Path.Combine(Path.GetTempPath(), $"tt-useredit-{Guid.NewGuid():N}.db");
        var sp = BuildServices(dbFile);
        try
        {
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var um = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var rm = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            await db.Database.EnsureCreatedAsync();
            foreach (var r in Roles.All) await rm.CreateAsync(new IdentityRole(r));
            await body(scope.ServiceProvider, db, um, rm);
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
