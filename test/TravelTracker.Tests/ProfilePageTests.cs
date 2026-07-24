using System.Security.Claims;
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
using TravelTracker.Web.Models;
using TravelTracker.Web.Pages.Account.Manage;
using TravelTracker.Web.Services;
using Xunit;

namespace TravelTracker.Tests;

// Drives the self-service ProfileModel: load, save, and (critically) that the
// sensitive identifiers are encrypted at rest, not stored in the clear.
public class ProfilePageTests
{
    private static readonly ISensitiveFieldProtector Protector =
        new SensitiveFieldProtector(DataProtectionProvider.Create("TravelTracker.Tests"));

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

    private static ProfileModel NewPage(AppUser signedInAs, UserManager<AppUser> um, AppDbContext db)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, signedInAs.Id) }, "Test"));
        var http = new DefaultHttpContext { User = principal };
        var ac = new ActionContext(http, new RouteData(), new PageActionDescriptor(), new ModelStateDictionary());
        return new ProfileModel(um, Protector, db)
        {
            PageContext = new PageContext(ac),
            TempData = new TempDataDictionary(http, new TestTempDataProvider())
        };
    }

    [Fact]
    public async Task OnPost_Saves_Profile_And_Encrypts_Passport()
    {
        await WithScope(async (db, um) =>
        {
            var user = await NewUser(um, "trav@example.com");

            var page = NewPage(user, um, db);
            page.Input = new TravelerProfileInput
            {
                PassportNumber = "X9988776",
                PassportExpiry = new DateOnly(2030, 1, 1),
                Nationality = "  United States  ",
                KnownTravelerNumber = "KTN123456",
                MobileNumber = "+1 555 010 2020",
                ReimbursementMethod = ReimbursementMethod.DirectDeposit,
            };

            Assert.IsType<RedirectToPageResult>(await page.OnPostAsync());

            var saved = await um.FindByIdAsync(user.Id);
            // Sensitive fields are ciphertext at rest...
            Assert.NotNull(saved!.PassportNumberProtected);
            Assert.NotEqual("X9988776", saved.PassportNumberProtected);
            Assert.NotEqual("KTN123456", saved.KnownTravelerNumberProtected);
            // ...but decrypt back to the entered values.
            Assert.Equal("X9988776", Protector.Unprotect(saved.PassportNumberProtected));
            Assert.Equal("KTN123456", Protector.Unprotect(saved.KnownTravelerNumberProtected));
            // Plaintext fields trimmed + stored.
            Assert.Equal("United States", saved.Nationality);
            Assert.Equal(new DateOnly(2030, 1, 1), saved.PassportExpiry);
            Assert.Equal(ReimbursementMethod.DirectDeposit, saved.ReimbursementMethod);
        });
    }

    [Fact]
    public async Task OnGet_Loads_And_Decrypts_Existing_Profile()
    {
        await WithScope(async (db, um) =>
        {
            var user = await NewUser(um, "trav2@example.com");
            user.PassportNumberProtected = Protector.Protect("P0001112");
            user.PassportExpiry = new DateOnly(2029, 6, 30);
            await um.UpdateAsync(user);

            var page = NewPage(user, um, db);
            Assert.IsType<PageResult>(await page.OnGetAsync());
            Assert.Equal("P0001112", page.Input.PassportNumber); // decrypted for the form
            Assert.Equal(new DateOnly(2029, 6, 30), page.Input.PassportExpiry);
        });
    }

    [Fact]
    public async Task OnPost_Clearing_A_Sensitive_Field_Stores_Null()
    {
        await WithScope(async (db, um) =>
        {
            var user = await NewUser(um, "trav3@example.com");
            user.PassportNumberProtected = Protector.Protect("WILLGO99");
            await um.UpdateAsync(user);

            var page = NewPage(user, um, db);
            page.Input = new TravelerProfileInput { PassportNumber = "   " }; // blanked
            Assert.IsType<RedirectToPageResult>(await page.OnPostAsync());

            Assert.Null((await um.FindByIdAsync(user.Id))!.PassportNumberProtected);
        });
    }

    private static async Task<AppUser> NewUser(UserManager<AppUser> um, string email)
    {
        var u = new AppUser { UserName = email, Email = email, DisplayName = email };
        var r = await um.CreateAsync(u, "Passw0rd!");
        Assert.True(r.Succeeded, string.Join("; ", r.Errors.Select(e => e.Description)));
        return u;
    }

    private static async Task WithScope(Func<AppDbContext, UserManager<AppUser>, Task> body)
    {
        var dbFile = Path.Combine(Path.GetTempPath(), $"tt-profile-{Guid.NewGuid():N}.db");
        var sp = BuildServices(dbFile);
        try
        {
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var um = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            await db.Database.EnsureCreatedAsync();
            await body(db, um);
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
