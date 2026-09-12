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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TravelTracker.Web.Auth;
using TravelTracker.Web.Data;
using TravelTracker.Web.Data.Entities;
using TravelTracker.Web.Pages.Admin.Users;
using TravelTracker.Web.Services;
using Xunit;

namespace TravelTracker.Tests;

// Drives the real CreateModel.OnPostAsync. An admin-created account starts with NO
// password: the user sets their own through a one-time link, so no credential the
// admin saw can ever be used to sign in.
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
        services.AddIdentityCore<AppUser>(o =>
            {
                o.Password.RequiredLength = 8;
                o.Password.RequireNonAlphanumeric = false;
                o.Password.RequireUppercase = false;
                o.User.RequireUniqueEmail = true;
                o.Tokens.PasswordResetTokenProvider = PasswordSetupTokenProvider.ProviderName;
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders()
            .AddTokenProvider<PasswordSetupTokenProvider>(PasswordSetupTokenProvider.ProviderName);

        return services.BuildServiceProvider();
    }

    private static PasswordSetupMailer NewMailer(UserManager<AppUser> um, RecordingEmailTemplateService email) =>
        new(um, email, NullLogger<PasswordSetupMailer>.Instance,
            Options.Create(new PasswordSetupTokenProviderOptions()));

    private static CreateModel NewPage(
        IServiceProvider sp, AppDbContext db, UserManager<AppUser> um, PasswordSetupMailer mailer)
    {
        var httpContext = new DefaultHttpContext { RequestServices = sp };
        var actionContext = new ActionContext(
            httpContext, new RouteData(), new PageActionDescriptor(), new ModelStateDictionary());
        return new CreateModel(db, um, mailer)
        {
            PageContext = new PageContext(actionContext),
            TempData = new TempDataDictionary(httpContext, new TestTempDataProvider()),
            Url = new FakeUrlHelper(actionContext)
        };
    }

    [Fact]
    public async Task OnPost_creates_a_passwordless_user_and_emails_a_setup_link()
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

            var email = new RecordingEmailTemplateService();
            var page = NewPage(scope.ServiceProvider, db, um, NewMailer(um, email));
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

            // No password exists yet — the account cannot be signed into.
            Assert.False(await um.HasPasswordAsync(user));

            var sent = Assert.Single(email.Sent);
            Assert.Equal(EmailTemplateKey.AccountSetup, sent.Key);
            Assert.Equal("jane@example.com", sent.Recipient);
            Assert.False(string.IsNullOrWhiteSpace(sent.Tokens["SetupLink"]));
            Assert.Equal("24 hours", sent.Tokens["Setup.ExpiresIn"]);

            Assert.False(string.IsNullOrWhiteSpace(page.TempData["SetupLink"] as string));
            Assert.True(page.TempData["SetupEmailed"] is true);
            Assert.Null(page.TempData["NewUserPassword"]);
        }
        finally
        {
            await sp.DisposeAsync();
            TestDatabase.Drop(TestDatabase.ConnectionStringFor(dbFile));
        }
    }

    // Regression: Add user and Invites are separate paths, and Add user used to let an
    // admin create an account that already had an open invite — stranding that invite
    // as a dead link.
    [Fact]
    public async Task OnPost_rejects_an_email_with_an_open_invite()
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

            db.Invitations.Add(new Invitation
            {
                Email = "pending@example.com",
                Role = Roles.Employee,
                TokenHash = "hash",
                InvitedByUserId = "admin",
                CreatedAt = DateTimeOffset.UtcNow,
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(7)
            });
            await db.SaveChangesAsync();

            var email = new RecordingEmailTemplateService();
            var page = NewPage(scope.ServiceProvider, db, um, NewMailer(um, email));
            page.Input = new CreateModel.InputModel
            {
                DisplayName = "Pending Person", Email = "pending@example.com", Role = Roles.Employee
            };

            var result = await page.OnPostAsync();

            Assert.IsType<PageResult>(result);
            Assert.False(page.ModelState.IsValid);
            Assert.Null(await um.FindByEmailAsync("pending@example.com"));
            Assert.Empty(email.Sent);
        }
        finally
        {
            await sp.DisposeAsync();
            TestDatabase.Drop(TestDatabase.ConnectionStringFor(dbFile));
        }
    }

    // An accepted (spent) invite must not block creating an account later.
    [Fact]
    public async Task OnPost_allows_an_email_whose_invite_was_already_accepted()
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

            db.Invitations.Add(new Invitation
            {
                Email = "spent@example.com",
                Role = Roles.Employee,
                TokenHash = "hash",
                InvitedByUserId = "admin",
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-9),
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(-2),
                AcceptedAt = DateTimeOffset.UtcNow.AddDays(-8)
            });
            await db.SaveChangesAsync();

            var email = new RecordingEmailTemplateService();
            var page = NewPage(scope.ServiceProvider, db, um, NewMailer(um, email));
            page.Input = new CreateModel.InputModel
            {
                DisplayName = "Spent Invite", Email = "spent@example.com", Role = Roles.Employee
            };

            var result = await page.OnPostAsync();

            Assert.IsType<RedirectToPageResult>(result);
            Assert.NotNull(await um.FindByEmailAsync("spent@example.com"));
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

            var email = new RecordingEmailTemplateService();
            var page = NewPage(scope.ServiceProvider, db, um, NewMailer(um, email));
            page.Input = new CreateModel.InputModel
            {
                DisplayName = "Second", Email = "dupe@example.com", Role = Roles.Employee
            };

            var result = await page.OnPostAsync();

            Assert.IsType<PageResult>(result);
            Assert.False(page.ModelState.IsValid);
            Assert.Equal(1, db.Users.Count(u => u.Email == "dupe@example.com"));
            Assert.Empty(email.Sent);
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
