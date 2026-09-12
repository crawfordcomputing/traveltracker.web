using System.Text;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.WebUtilities;
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

// Covers the admin-initiated password reset on Admin/Users/Index. An admin no longer
// generates a password: they issue a one-time link the user redeems themselves. The
// existing password keeps working until that happens, and the link expires and dies
// on first use.
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
                // Mirror the app: password links come from our own provider.
                o.Tokens.PasswordResetTokenProvider = PasswordSetupTokenProvider.ProviderName;
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders()
            .AddTokenProvider<PasswordSetupTokenProvider>(PasswordSetupTokenProvider.ProviderName);
        return services.BuildServiceProvider();
    }

    private static void Cleanup(string dbFile)
    {
        TestDatabase.Drop(TestDatabase.ConnectionStringFor(dbFile));
    }

    private static PasswordSetupMailer NewMailer(UserManager<AppUser> um, RecordingEmailTemplateService email) =>
        new(um, email, NullLogger<PasswordSetupMailer>.Instance,
            Options.Create(new PasswordSetupTokenProviderOptions()));

    private static IndexModel NewIndexPage(
        IServiceProvider sp, AppDbContext db, UserManager<AppUser> um, PasswordSetupMailer mailer)
    {
        var httpContext = new DefaultHttpContext { RequestServices = sp };
        var actionContext = new ActionContext(
            httpContext, new RouteData(), new PageActionDescriptor(), new ModelStateDictionary());
        return new IndexModel(db, um, mailer)
        {
            PageContext = new PageContext(actionContext),
            TempData = new TempDataDictionary(httpContext, new TestTempDataProvider()),
            Url = new FakeUrlHelper(actionContext)
        };
    }

    [Fact]
    public async Task Reset_emails_a_link_and_leaves_the_current_password_working()
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

            var email = new RecordingEmailTemplateService();
            var page = NewIndexPage(scope.ServiceProvider, db, um, NewMailer(um, email));
            var result = await page.OnPostResetPasswordAsync(user.Id);

            Assert.IsType<RedirectToPageResult>(result);

            // A reset link was mailed to the user — no password is handed to the admin.
            var sent = Assert.Single(email.Sent);
            Assert.Equal(EmailTemplateKey.PasswordReset, sent.Key);
            Assert.Equal("u@example.com", sent.Recipient);
            Assert.False(string.IsNullOrWhiteSpace(sent.Tokens["ResetLink"]));

            // The link is surfaced for the admin as a fallback, and nothing else.
            Assert.False(string.IsNullOrWhiteSpace(page.TempData["SetupLink"] as string));
            Assert.True(page.TempData["SetupEmailed"] is true);
            Assert.Null(page.TempData["NewUserPassword"]);

            // Current credential is untouched until the user redeems the link.
            var reloaded = await um.FindByIdAsync(user.Id);
            Assert.True(await um.CheckPasswordAsync(reloaded!, "OldPassw0rd!"));
        }
        finally { await sp.DisposeAsync(); Cleanup(dbFile); }
    }

    [Fact]
    public async Task Reset_still_reports_success_when_the_mail_fails()
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

            var email = new RecordingEmailTemplateService { Throw = new InvalidOperationException("smtp down") };
            var page = NewIndexPage(scope.ServiceProvider, db, um, NewMailer(um, email));
            var result = await page.OnPostResetPasswordAsync(user.Id);

            // Mail outage must not fail the request: the link is still on screen.
            Assert.IsType<RedirectToPageResult>(result);
            Assert.False(string.IsNullOrWhiteSpace(page.TempData["SetupLink"] as string));
            Assert.True(page.TempData["SetupEmailed"] is false);
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

            var email = new RecordingEmailTemplateService();
            var page = NewIndexPage(scope.ServiceProvider, db, um, NewMailer(um, email));
            var result = await page.OnPostResetPasswordAsync("does-not-exist");

            Assert.IsType<NotFoundResult>(result);
            Assert.Empty(email.Sent);
        }
        finally { await sp.DisposeAsync(); Cleanup(dbFile); }
    }

    // The security property behind option A: an admin-issued link works once and is
    // then dead, because redeeming it rotates the security stamp it is bound to.
    [Fact]
    public async Task Setup_link_token_works_once_then_is_rejected()
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

            var mailer = NewMailer(um, new RecordingEmailTemplateService());
            var encoded = await mailer.CreateTokenAsync(user);
            var token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(encoded));

            var first = await um.ResetPasswordAsync(user, token, "NewPassw0rd!");
            Assert.True(first.Succeeded);

            var replay = await um.ResetPasswordAsync(user, token, "Another-Pw1!");
            Assert.False(replay.Succeeded);

            Assert.True(await um.CheckPasswordAsync(user, "NewPassw0rd!"));
            Assert.False(await um.CheckPasswordAsync(user, "OldPassw0rd!"));
        }
        finally { await sp.DisposeAsync(); Cleanup(dbFile); }
    }

    [Fact]
    public void Link_lifetime_defaults_to_24_hours()
    {
        var options = new PasswordSetupTokenProviderOptions();
        Assert.Equal(TimeSpan.FromHours(24), options.TokenLifespan);
        Assert.Equal(PasswordSetupTokenProvider.ProviderName, options.Name);
    }

    private sealed class TestTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) =>
            new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
}
