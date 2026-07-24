using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using TravelTracker.Web.Data.Entities;

namespace TravelTracker.Web.Data;

public static class DbInitializer
{
    // Applies migrations on startup and seeds baseline data:
    //   - the roles in Roles.All (Employee / Arranger / Manager / Finance / Admin)
    //   - a "General" department
    //   - a config-driven admin user promoted to the Admin role
    // Idempotent: safe to run on every startup / deploy.
    public static async Task InitializeAsync(IServiceProvider services, IConfiguration config)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();

        // No IHostEnvironment in bare unit-test containers -> treat as development
        // (isolated seeding tests are allowed the convenience password).
        var env = sp.GetService<IHostEnvironment>();
        var isDevelopment = env?.IsDevelopment() ?? true;

        // Applies any pending migrations and creates the database if it does not
        // yet exist (SQL Server / Azure SQL).
        await db.Database.MigrateAsync();

        var roleManager = sp.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = sp.GetRequiredService<UserManager<AppUser>>();
        await SeedAsync(db, roleManager, userManager, config, isDevelopment);
    }

    public static async Task SeedAsync(
        AppDbContext db,
        RoleManager<IdentityRole> roleManager,
        UserManager<AppUser> userManager,
        IConfiguration config,
        bool isDevelopment)
    {
        foreach (var role in Roles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));
        }

        if (!await db.Departments.AnyAsync())
        {
            db.Departments.Add(new Department { Name = "General" });
            await db.SaveChangesAsync();
        }

        if (!await db.Countries.AnyAsync())
        {
            db.Countries.AddRange(ReferenceData.Countries
                .Select(c => new Country { Code = c.Code, Name = c.Name }));
            await db.SaveChangesAsync();
        }

        if (!await db.UsStates.AnyAsync())
        {
            db.UsStates.AddRange(ReferenceData.UsStates
                .Select(s => new UsState { Code = s.Code, Name = s.Name }));
            await db.SaveChangesAsync();
        }

        // Effective-dated mileage rate table (US business auto). Idempotent so a
        // fresh install can compute mileage out of the box; admins add more via UI.
        if (!await db.MileageRates.AnyAsync())
        {
            db.MileageRates.AddRange(MileageRateSeed.BuildAll());
            await db.SaveChangesAsync();
        }

        var adminEmail = config["Seed:AdminEmail"] ?? "admin@example.com";

        // The seed password must never be a committed default. Require it to be
        // supplied out-of-band (env var / user-secrets / secret store) outside of
        // Development; only fall back to a throwaway value for local dev.
        var adminPassword = config["Seed:AdminPassword"];
        if (string.IsNullOrWhiteSpace(adminPassword))
        {
            if (isDevelopment)
            {
                adminPassword = "Admin123!"; // dev-only convenience; never used in non-dev
            }
            else
            {
                throw new InvalidOperationException(
                    "Seed:AdminPassword is not configured. Provide it via an environment " +
                    "variable, user-secrets, or a secret store before deploying to a " +
                    "non-Development environment.");
            }
        }

        if (await userManager.FindByEmailAsync(adminEmail) is null)
        {
            var dept = await db.Departments.FirstAsync();
            var admin = new AppUser
            {
                UserName = adminEmail,
                Email = adminEmail,
                EmailConfirmed = true,
                DisplayName = "Default Admin",
                DepartmentId = dept.Id
            };

            var result = await userManager.CreateAsync(admin, adminPassword);
            if (result.Succeeded)
                await userManager.AddToRoleAsync(admin, Roles.Admin);
            else
                throw new InvalidOperationException(
                    "Failed to seed admin user: " +
                    string.Join("; ", result.Errors.Select(e => e.Description)));
        }
    }
}
