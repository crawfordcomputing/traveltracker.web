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
using TravelTracker.Web.Pages.Account.Manage;
using Xunit;

namespace TravelTracker.Tests;

// Drives the signed-in ChangePassword handler: the correct current password rotates
// the credential; a wrong current password is refused and leaves it unchanged.
public class PasswordChangeTests
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
        services.AddAppDatabase(config);
        // Full Identity (not Core) so the cookie schemes exist and RefreshSignInAsync
        // can re-issue the sign-in after a successful change.
        services.AddIdentity<AppUser, IdentityRole>(o =>
            {
                o.Password.RequiredLength = 8;
                o.Password.RequireNonAlphanumeric = false;
                o.Password.RequireUppercase = false;
                o.SignIn.RequireConfirmedAccount = false;
                o.User.RequireUniqueEmail = true;
            })
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders();

        return services.BuildServiceProvider();
    }

    private static void Cleanup(string dbFile)
    {
        TestDatabase.Drop(TestDatabase.ConnectionStringFor(dbFile));
    }

    private static async Task<ChangePasswordModel> NewPageForUserAsync(
        IServiceProvider sp, AppUser user)
    {
        var um = sp.GetRequiredService<UserManager<AppUser>>();
        var signIn = sp.GetRequiredService<SignInManager<AppUser>>();

        var principal = await signIn.CreateUserPrincipalAsync(user);
        var httpContext = new DefaultHttpContext { RequestServices = sp, User = principal };
        var actionContext = new ActionContext(
            httpContext, new RouteData(), new PageActionDescriptor(), new ModelStateDictionary());
        return new ChangePasswordModel(um, signIn)
        {
            PageContext = new PageContext(actionContext),
            TempData = new TempDataDictionary(httpContext, new TestTempDataProvider())
        };
    }

    [Fact]
    public async Task Correct_current_password_changes_it()
    {
        var dbFile = Path.Combine(Path.GetTempPath(), $"tt-chg-{Guid.NewGuid():N}.db");
        var sp = BuildServices(dbFile);
        try
        {
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var um = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            await db.Database.EnsureCreatedAsync();

            var user = new AppUser { UserName = "u@example.com", Email = "u@example.com", DisplayName = "U" };
            await um.CreateAsync(user, "OldPassw0rd!");

            var page = await NewPageForUserAsync(scope.ServiceProvider, user);
            // Set in the test body (not the helper): AsyncLocal assignments don't flow
            // back out of an awaited method, so RefreshSignInAsync would see it null.
            scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = page.HttpContext;
            page.Input = new ChangePasswordModel.InputModel
            {
                CurrentPassword = "OldPassw0rd!",
                NewPassword = "BrandNew1!",
                ConfirmPassword = "BrandNew1!"
            };
            var result = await page.OnPostAsync();

            Assert.IsType<RedirectToPageResult>(result);
            var reloaded = await um.FindByIdAsync(user.Id);
            Assert.True(await um.CheckPasswordAsync(reloaded!, "BrandNew1!"));
            Assert.False(await um.CheckPasswordAsync(reloaded!, "OldPassw0rd!"));
        }
        finally { await sp.DisposeAsync(); Cleanup(dbFile); }
    }

    [Fact]
    public async Task Wrong_current_password_is_rejected()
    {
        var dbFile = Path.Combine(Path.GetTempPath(), $"tt-chg-{Guid.NewGuid():N}.db");
        var sp = BuildServices(dbFile);
        try
        {
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var um = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            await db.Database.EnsureCreatedAsync();

            var user = new AppUser { UserName = "u@example.com", Email = "u@example.com", DisplayName = "U" };
            await um.CreateAsync(user, "OldPassw0rd!");

            var page = await NewPageForUserAsync(scope.ServiceProvider, user);
            page.Input = new ChangePasswordModel.InputModel
            {
                CurrentPassword = "WrongPassw0rd!",
                NewPassword = "BrandNew1!",
                ConfirmPassword = "BrandNew1!"
            };
            var result = await page.OnPostAsync();

            Assert.IsType<PageResult>(result);
            Assert.False(page.ModelState.IsValid);
            var reloaded = await um.FindByIdAsync(user.Id);
            Assert.True(await um.CheckPasswordAsync(reloaded!, "OldPassw0rd!")); // unchanged
        }
        finally { await sp.DisposeAsync(); Cleanup(dbFile); }
    }

    private sealed class TestTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) =>
            new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
}
