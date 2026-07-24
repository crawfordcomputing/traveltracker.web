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
using TravelTracker.Web.Domain;
using TravelTracker.Web.Pages.Admin.Users;
using Xunit;

namespace TravelTracker.Tests;

// Covers the admin-initiated password reset on Admin/Users/Index: a fresh temporary
// password is set (old one stops working, new one shown once), and the reset rotates
// the security stamp so live sessions are invalidated.
public class AdminPasswordResetTests
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
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders();
        return services.BuildServiceProvider();
    }

    private static void Cleanup(string dbFile)
    {
        TestDatabase.Drop(TestDatabase.ConnectionStringFor(dbFile));
    }

    private static IndexModel NewIndexPage(IServiceProvider sp, AppDbContext db, UserManager<AppUser> um)
    {
        var httpContext = new DefaultHttpContext { RequestServices = sp };
        var actionContext = new ActionContext(
            httpContext, new RouteData(), new PageActionDescriptor(), new ModelStateDictionary());
        return new IndexModel(db, um)
        {
            PageContext = new PageContext(actionContext),
            TempData = new TempDataDictionary(httpContext, new TestTempDataProvider())
        };
    }

    [Fact]
    public async Task Reset_sets_new_temp_password_and_invalidates_old()
    {
        var dbFile = Path.Combine(Path.GetTempPath(), $"tt-apr-{Guid.NewGuid():N}.db");
        var sp = BuildServices(dbFile);
        try
        {
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var um = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            await db.Database.EnsureCreatedAsync();

            var user = new AppUser { UserName = "u@example.com", Email = "u@example.com", DisplayName = "U" };
            await um.CreateAsync(user, "OldPassw0rd!");
            var stampBefore = await um.GetSecurityStampAsync(user);

            var page = NewIndexPage(scope.ServiceProvider, db, um);
            var result = await page.OnPostResetPasswordAsync(user.Id);

            Assert.IsType<RedirectToPageResult>(result);

            // The one-time password is surfaced to the admin and satisfies the policy.
            var shown = page.TempData["NewUserPassword"] as string;
            Assert.False(string.IsNullOrWhiteSpace(shown));
            Assert.True(shown!.Length >= 8);
            Assert.Equal("u@example.com", page.TempData["NewUserEmail"] as string);
            Assert.Equal("Password reset.", page.TempData["PasswordHeadline"] as string);

            var reloaded = await um.FindByIdAsync(user.Id);
            Assert.True(await um.CheckPasswordAsync(reloaded!, shown));           // new works
            Assert.False(await um.CheckPasswordAsync(reloaded!, "OldPassw0rd!")); // old dead
            Assert.NotEqual(stampBefore, await um.GetSecurityStampAsync(reloaded!)); // sessions revoked
        }
        finally { await sp.DisposeAsync(); Cleanup(dbFile); }
    }

    [Fact]
    public async Task Reset_returns_NotFound_for_unknown_user()
    {
        var dbFile = Path.Combine(Path.GetTempPath(), $"tt-apr-{Guid.NewGuid():N}.db");
        var sp = BuildServices(dbFile);
        try
        {
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var um = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            await db.Database.EnsureCreatedAsync();

            var page = NewIndexPage(scope.ServiceProvider, db, um);
            var result = await page.OnPostResetPasswordAsync("does-not-exist");

            Assert.IsType<NotFoundResult>(result);
        }
        finally { await sp.DisposeAsync(); Cleanup(dbFile); }
    }

    [Fact]
    public void Generated_temp_password_satisfies_policy()
    {
        for (var i = 0; i < 50; i++)
        {
            var pw = TempPassword.Generate();
            Assert.True(pw.Length >= 16);
            Assert.Contains(pw, char.IsLower);
            Assert.Contains(pw, char.IsDigit);
        }
    }

    private sealed class TestTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) =>
            new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
}
