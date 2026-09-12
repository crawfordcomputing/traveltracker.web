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

// Covers M1.5 §2 user deactivation: AdminGuard (last-active-admin), AppSignInManager
// refusing a deactivated account, and the Users/Index toggle handler round-trip.
public class DeactivationTests
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
        services.AddAuthentication();
        services.AddAppDatabase(config);
        services.AddIdentityCore<AppUser>(o =>
            {
                o.Password.RequiredLength = 8;
                o.Password.RequireNonAlphanumeric = false;
                o.Password.RequireUppercase = false;
                o.SignIn.RequireConfirmedAccount = false;
                o.User.RequireUniqueEmail = true;
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddSignInManager<AppSignInManager>()
            .AddDefaultTokenProviders();

        return services.BuildServiceProvider();
    }

    private static void Cleanup(string dbFile)
    {
        TestDatabase.Drop(TestDatabase.ConnectionStringFor(dbFile));
    }

    private static async Task<AppUser> CreateUserAsync(
        UserManager<AppUser> um, string email, string? role = null)
    {
        var user = new AppUser { UserName = email, Email = email, DisplayName = email, EmailConfirmed = true };
        var result = await um.CreateAsync(user, "Passw0rd!");
        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(e => e.Description)));
        if (role is not null) await um.AddToRoleAsync(user, role);
        return user;
    }

    private static IndexModel NewIndexPage(IServiceProvider sp, AppDbContext db, UserManager<AppUser> um)
    {
        var httpContext = new DefaultHttpContext { RequestServices = sp };
        var actionContext = new ActionContext(
            httpContext, new RouteData(), new PageActionDescriptor(), new ModelStateDictionary());
        // Deactivation never issues a password link; a no-op mailer satisfies the ctor.
        var mailer = new TravelTracker.Web.Services.PasswordSetupMailer(
            um, new NoopEmailTemplateService(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<TravelTracker.Web.Services.PasswordSetupMailer>.Instance,
            Microsoft.Extensions.Options.Options.Create(new TravelTracker.Web.Auth.PasswordSetupTokenProviderOptions()));

        return new IndexModel(db, um, mailer)
        {
            PageContext = new PageContext(actionContext),
            TempData = new TempDataDictionary(httpContext, new TestTempDataProvider())
        };
    }

    [Fact]
    public async Task AdminGuard_Blocks_Last_Active_Admin_Allows_When_Two()
    {
        var dbFile = Path.Combine(Path.GetTempPath(), $"tt-deact-{Guid.NewGuid():N}.db");
        var sp = BuildServices(dbFile);
        try
        {
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var um = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var rm = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            await db.Database.EnsureCreatedAsync();
            foreach (var r in Roles.All) await rm.CreateAsync(new IdentityRole(r));

            var admin1 = await CreateUserAsync(um, "admin1@example.com", Roles.Admin);
            Assert.True(await AdminGuard.IsLastActiveAdminAsync(um, admin1));

            var admin2 = await CreateUserAsync(um, "admin2@example.com", Roles.Admin);
            Assert.False(await AdminGuard.IsLastActiveAdminAsync(um, admin1));

            // Demoting an already-inactive admin is safe even if only one remains active.
            admin2.IsActive = false;
            await um.UpdateAsync(admin2);
            Assert.True(await AdminGuard.IsLastActiveAdminAsync(um, admin1));
            Assert.False(await AdminGuard.IsLastActiveAdminAsync(um, admin2));
        }
        finally { await sp.DisposeAsync(); Cleanup(dbFile); }
    }

    [Fact]
    public async Task Deactivated_User_Cannot_Sign_In()
    {
        var dbFile = Path.Combine(Path.GetTempPath(), $"tt-deact-{Guid.NewGuid():N}.db");
        var sp = BuildServices(dbFile);
        try
        {
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var um = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var rm = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var signIn = scope.ServiceProvider.GetRequiredService<SignInManager<AppUser>>();
            await db.Database.EnsureCreatedAsync();
            foreach (var r in Roles.All) await rm.CreateAsync(new IdentityRole(r));

            var user = await CreateUserAsync(um, "leaver@example.com", Roles.Employee);
            Assert.IsType<AppSignInManager>(signIn);
            Assert.True(await signIn.CanSignInAsync(user));

            user.IsActive = false;
            await um.UpdateAsync(user);

            Assert.False(await signIn.CanSignInAsync(user));
        }
        finally { await sp.DisposeAsync(); Cleanup(dbFile); }
    }

    [Fact]
    public async Task Toggle_Deactivates_User_And_Bumps_Security_Stamp()
    {
        var dbFile = Path.Combine(Path.GetTempPath(), $"tt-deact-{Guid.NewGuid():N}.db");
        var sp = BuildServices(dbFile);
        try
        {
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var um = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var rm = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            await db.Database.EnsureCreatedAsync();
            foreach (var r in Roles.All) await rm.CreateAsync(new IdentityRole(r));

            await CreateUserAsync(um, "admin@example.com", Roles.Admin); // keeps an active admin around
            var user = await CreateUserAsync(um, "worker@example.com", Roles.Employee);
            var stampBefore = await um.GetSecurityStampAsync(user);

            var page = NewIndexPage(scope.ServiceProvider, db, um);
            var result = await page.OnPostToggleActiveAsync(user.Id);

            Assert.IsType<RedirectToPageResult>(result);
            var reloaded = await um.FindByIdAsync(user.Id);
            Assert.False(reloaded!.IsActive);
            Assert.NotEqual(stampBefore, await um.GetSecurityStampAsync(reloaded));
        }
        finally { await sp.DisposeAsync(); Cleanup(dbFile); }
    }

    [Fact]
    public async Task Toggle_Blocks_Deactivating_Last_Active_Admin()
    {
        var dbFile = Path.Combine(Path.GetTempPath(), $"tt-deact-{Guid.NewGuid():N}.db");
        var sp = BuildServices(dbFile);
        try
        {
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var um = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var rm = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            await db.Database.EnsureCreatedAsync();
            foreach (var r in Roles.All) await rm.CreateAsync(new IdentityRole(r));

            var admin = await CreateUserAsync(um, "onlyadmin@example.com", Roles.Admin);

            var page = NewIndexPage(scope.ServiceProvider, db, um);
            var result = await page.OnPostToggleActiveAsync(admin.Id);

            Assert.IsType<RedirectToPageResult>(result);
            Assert.NotNull(page.TempData["Error"]);
            var reloaded = await um.FindByIdAsync(admin.Id);
            Assert.True(reloaded!.IsActive); // still active — guard held
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
