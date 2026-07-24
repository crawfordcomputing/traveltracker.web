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
using Xunit;

namespace TravelTracker.Tests;

// Drives the real CreateModel.OnPostAsync so the generated password is exercised
// against the configured Identity password policy (must contain lower + digit).
public class AdminUserCreateTests
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
        // Match the app's password policy so the test proves the generator complies.
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

    private static CreateModel NewPage(IServiceProvider sp, AppDbContext db, UserManager<AppUser> um)
    {
        var httpContext = new DefaultHttpContext { RequestServices = sp };
        var actionContext = new ActionContext(
            httpContext, new RouteData(), new PageActionDescriptor(), new ModelStateDictionary());
        return new CreateModel(db, um)
        {
            PageContext = new PageContext(actionContext),
            TempData = new TempDataDictionary(httpContext, new TestTempDataProvider())
        };
    }

    [Fact]
    public async Task OnPost_Creates_User_With_Role_And_Shows_Password_Once()
    {
        var dbFile = Path.Combine(Path.GetTempPath(), $"tt-create-{Guid.NewGuid():N}.db");
        var sp = BuildServices(dbFile);
        try
        {
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var um = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var rm = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            await db.Database.EnsureCreatedAsync();
            foreach (var r in Roles.All) await rm.CreateAsync(new IdentityRole(r));

            var page = NewPage(scope.ServiceProvider, db, um);
            page.Input = new CreateModel.InputModel
            {
                DisplayName = "Jane Doe",
                Email = "jane@example.com",
                Role = Roles.Manager
            };

            var result = await page.OnPostAsync();

            Assert.IsType<RedirectToPageResult>(result);
            var user = await um.FindByEmailAsync("jane@example.com");
            Assert.NotNull(user);
            Assert.Equal("Jane Doe", user!.DisplayName);
            Assert.True(await um.IsInRoleAsync(user, Roles.Manager));

            var shown = page.TempData["NewUserPassword"] as string;
            Assert.False(string.IsNullOrWhiteSpace(shown));
            Assert.True(shown!.Length >= 8);
        }
        finally
        {
            await sp.DisposeAsync();
            TestDatabase.Drop(TestDatabase.ConnectionStringFor(dbFile));
        }
    }

    [Fact]
    public async Task OnPost_Rejects_Duplicate_Email()
    {
        var dbFile = Path.Combine(Path.GetTempPath(), $"tt-create-{Guid.NewGuid():N}.db");
        var sp = BuildServices(dbFile);
        try
        {
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var um = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var rm = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            await db.Database.EnsureCreatedAsync();
            foreach (var r in Roles.All) await rm.CreateAsync(new IdentityRole(r));
            await um.CreateAsync(new AppUser { UserName = "dupe@example.com", Email = "dupe@example.com", DisplayName = "Existing" }, "Existing-1!");

            var page = NewPage(scope.ServiceProvider, db, um);
            page.Input = new CreateModel.InputModel
            {
                DisplayName = "Second", Email = "dupe@example.com", Role = Roles.Employee
            };

            var result = await page.OnPostAsync();

            Assert.IsType<PageResult>(result);
            Assert.False(page.ModelState.IsValid);
            Assert.Equal(1, db.Users.Count(u => u.Email == "dupe@example.com"));
        }
        finally
        {
            await sp.DisposeAsync();
            TestDatabase.Drop(TestDatabase.ConnectionStringFor(dbFile));
        }
    }

    private sealed class TestTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) =>
            new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
}
