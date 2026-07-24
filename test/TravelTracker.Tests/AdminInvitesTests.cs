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
using TravelTracker.Web.Pages.Admin.Invites;
using Xunit;

namespace TravelTracker.Tests;

// Drives the real Admin/Invites IndexModel: listing, create (happy + each
// duplicate/validation guard), and revoke.
public class AdminInvitesTests
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

    private static IndexModel NewPage(IServiceProvider sp, AppDbContext db, UserManager<AppUser> um)
    {
        var http = new DefaultHttpContext
        {
            RequestServices = sp,
            User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.NameIdentifier, "admin-id") }, "Test"))
        };
        var ac = new ActionContext(http, new RouteData(), new PageActionDescriptor(), new ModelStateDictionary());
        return new IndexModel(db, um)
        {
            PageContext = new PageContext(ac),
            TempData = new TempDataDictionary(http, new TestTempDataProvider()),
            Url = new FakeUrlHelper(ac)
        };
    }

    [Fact]
    public async Task OnPostCreate_Adds_Invitation_And_Surfaces_Link()
    {
        await WithScope(async (sp, db, um) =>
        {
            var page = NewPage(sp, db, um);
            page.Input = new IndexModel.InputModel
            {
                Email = "  new@example.com  ", Role = Roles.Manager, ExpiresInDays = 5
            };

            var result = await page.OnPostCreateAsync();

            Assert.IsType<RedirectToPageResult>(result);
            var invite = await db.Invitations.SingleAsync();
            Assert.Equal("new@example.com", invite.Email); // trimmed
            Assert.Equal(Roles.Manager, invite.Role);
            Assert.Null(invite.AcceptedAt);
            Assert.NotEqual(default, invite.TokenHash.Length);
            Assert.Equal("new@example.com", page.TempData["InviteEmail"]);
            Assert.False(string.IsNullOrEmpty(page.TempData["InviteLink"] as string));
        });
    }

    [Fact]
    public async Task OnPostCreate_Rejects_Unknown_Role()
    {
        await WithScope(async (sp, db, um) =>
        {
            var page = NewPage(sp, db, um);
            page.Input = new IndexModel.InputModel { Email = "x@example.com", Role = "Wizard" };

            Assert.IsType<PageResult>(await page.OnPostCreateAsync());
            Assert.False(page.ModelState.IsValid);
            Assert.Equal(0, await db.Invitations.CountAsync());
        });
    }

    [Fact]
    public async Task OnPostCreate_Rejects_Existing_User_Email()
    {
        await WithScope(async (sp, db, um) =>
        {
            await um.CreateAsync(
                new AppUser { UserName = "taken@example.com", Email = "taken@example.com", DisplayName = "Taken" },
                "Emp-12345!");

            var page = NewPage(sp, db, um);
            page.Input = new IndexModel.InputModel { Email = "taken@example.com", Role = Roles.Employee };

            Assert.IsType<PageResult>(await page.OnPostCreateAsync());
            Assert.True(page.ModelState.ContainsKey("Input.Email"));
            Assert.Equal(0, await db.Invitations.CountAsync());
        });
    }

    [Fact]
    public async Task OnPostCreate_Rejects_Duplicate_Open_Invite()
    {
        await WithScope(async (sp, db, um) =>
        {
            db.Invitations.Add(new Invitation
            {
                Email = "dupe@example.com", Role = Roles.Employee,
                TokenHash = InviteTokens.Hash(InviteTokens.NewToken()),
                CreatedAt = DateTimeOffset.UtcNow, ExpiresAt = DateTimeOffset.UtcNow.AddDays(3)
            });
            await db.SaveChangesAsync();

            var page = NewPage(sp, db, um);
            page.Input = new IndexModel.InputModel { Email = "dupe@example.com", Role = Roles.Employee };

            Assert.IsType<PageResult>(await page.OnPostCreateAsync());
            Assert.True(page.ModelState.ContainsKey("Input.Email"));
            Assert.Equal(1, await db.Invitations.CountAsync()); // no second invite
        });
    }

    [Fact]
    public async Task OnGet_Lists_Invites_With_Expired_Flag()
    {
        await WithScope(async (sp, db, um) =>
        {
            db.Invitations.Add(new Invitation
            {
                Email = "expired@example.com", Role = Roles.Employee,
                TokenHash = "H1", CreatedAt = DateTimeOffset.UtcNow.AddDays(-10),
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1) // past
            });
            db.Invitations.Add(new Invitation
            {
                Email = "live@example.com", Role = Roles.Employee,
                TokenHash = "H2", CreatedAt = DateTimeOffset.UtcNow,
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(3)
            });
            await db.SaveChangesAsync();

            var page = NewPage(sp, db, um);
            Assert.IsType<PageResult>(await page.OnGetAsync());

            Assert.Equal(2, page.Invites.Count);
            Assert.True(page.Invites.Single(r => r.Email == "expired@example.com").Expired);
            Assert.False(page.Invites.Single(r => r.Email == "live@example.com").Expired);
        });
    }

    [Fact]
    public async Task OnPostRevoke_Removes_Open_Invite_But_Not_Accepted()
    {
        await WithScope(async (sp, db, um) =>
        {
            var open = new Invitation
            {
                Email = "open@example.com", Role = Roles.Employee, TokenHash = "H3",
                CreatedAt = DateTimeOffset.UtcNow, ExpiresAt = DateTimeOffset.UtcNow.AddDays(3)
            };
            var accepted = new Invitation
            {
                Email = "used@example.com", Role = Roles.Employee, TokenHash = "H4",
                CreatedAt = DateTimeOffset.UtcNow, ExpiresAt = DateTimeOffset.UtcNow.AddDays(3),
                AcceptedAt = DateTimeOffset.UtcNow
            };
            db.Invitations.AddRange(open, accepted);
            await db.SaveChangesAsync();

            var page = NewPage(sp, db, um);
            Assert.IsType<RedirectToPageResult>(await page.OnPostRevokeAsync(open.Id));
            Assert.IsType<RedirectToPageResult>(await page.OnPostRevokeAsync(accepted.Id));

            db.ChangeTracker.Clear();
            Assert.Null(await db.Invitations.FindAsync(open.Id));       // revoked
            Assert.NotNull(await db.Invitations.FindAsync(accepted.Id)); // accepted invites are kept
        });
    }

    private static async Task WithScope(
        Func<IServiceProvider, AppDbContext, UserManager<AppUser>, Task> body)
    {
        var dbFile = Path.Combine(Path.GetTempPath(), $"tt-invites-{Guid.NewGuid():N}.db");
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

    private sealed class TestTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
}
