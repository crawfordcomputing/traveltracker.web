using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TravelTracker.Web.Data;
using TravelTracker.Web.Data.Entities;
using Xunit;

namespace TravelTracker.Tests;

public class SeedTests
{
    // Builds a DI container with the DB provider + Identity core services so
    // DbInitializer (which needs RoleManager/UserManager) can run in isolation.
    private static (ServiceProvider provider, IConfiguration config) BuildServices(string dbFile)
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
        services.AddIdentityCore<AppUser>()
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<AppDbContext>();

        return (services.BuildServiceProvider(), config);
    }

    private static void Cleanup(string dbFile)
    {
        TestDatabase.Drop(TestDatabase.ConnectionStringFor(dbFile));
    }

    [Fact]
    public async Task Initialize_Seeds_Roles_Admin_And_Department()
    {
        var dbFile = Path.Combine(Path.GetTempPath(), $"tt-test-{Guid.NewGuid():N}.db");
        var (provider, config) = BuildServices(dbFile);
        try
        {
            await DbInitializer.InitializeAsync(provider, config);

            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

            Assert.Equal(1, await db.Departments.CountAsync());

            foreach (var role in Roles.All)
                Assert.True(await roleManager.RoleExistsAsync(role), $"role {role} missing");

            var admin = await userManager.FindByEmailAsync("admin@example.com");
            Assert.NotNull(admin);
            Assert.True(await userManager.IsInRoleAsync(admin!, Roles.Admin));
            Assert.NotNull(admin!.DepartmentId);
        }
        finally
        {
            await provider.DisposeAsync();
            Cleanup(dbFile);
        }
    }

    [Fact]
    public async Task Initialize_Is_Idempotent()
    {
        var dbFile = Path.Combine(Path.GetTempPath(), $"tt-test-{Guid.NewGuid():N}.db");
        var (provider, config) = BuildServices(dbFile);
        try
        {
            await DbInitializer.InitializeAsync(provider, config);
            await DbInitializer.InitializeAsync(provider, config); // must not duplicate

            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

            Assert.Equal(1, await db.Departments.CountAsync());
            Assert.Equal(1, await db.Users.CountAsync());
            Assert.Equal(Roles.All.Length, await roleManager.Roles.CountAsync());
        }
        finally
        {
            await provider.DisposeAsync();
            Cleanup(dbFile);
        }
    }
}
