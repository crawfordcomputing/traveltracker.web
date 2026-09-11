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
using Microsoft.Extensions.Logging.Abstractions;
using TravelTracker.Web.Data;
using TravelTracker.Web.Data.Entities;
using TravelTracker.Web.Pages.Admin.EmailTemplates;
using TravelTracker.Web.Services;
using TravelTracker.Web.Services.Email;
using Xunit;

namespace TravelTracker.Tests;

// ADR-0005 template service (resolution order, runtime fallback, audit columns)
// and the admin Edit page (save + sanitize, validation, reset). LocalDB-backed.
public class EmailTemplateServiceTests
{
    private const string AdminId = "admin-id";

    private static readonly Dictionary<string, string?> InternalExampleCom = new()
    {
        ["Email:InternalDomains:0"] = "example.com",
    };

    private static readonly Dictionary<string, string?> ResetTokens = new()
    {
        ["ResetLink"] = "https://app/reset?token=T",
    };

    private static void AddOverride(AppDbContext db, EmailTemplateKey key, EmailAudience audience,
        string subject, string body) =>
        db.EmailTemplates.Add(new EmailTemplate
        {
            Key = key, Audience = audience, Subject = subject, HtmlBody = body,
            UpdatedAt = DateTimeOffset.UtcNow, UpdatedById = AdminId,
        });

    // ---- Resolution order -------------------------------------------------------

    [Fact]
    public async Task No_Override_Sends_Built_In_Default()
    {
        await WithDb(async (db, _) =>
        {
            var mail = new RecordingEmailSender();
            await EmailTemplateServices.Real(db, mail).SendAsync(EmailTemplateKey.PasswordReset, "a@example.com", ResetTokens);

            Assert.Equal("Reset your Travel Tracker password", Assert.Single(mail.Sent).Subject);
        });
    }

    [Fact]
    public async Task Audience_Override_Beats_Any_Override_Beats_Default()
    {
        await WithDb(async (db, _) =>
        {
            AddOverride(db, EmailTemplateKey.PasswordReset, EmailAudience.Any, "ANY", "<a href=\"{{ResetLink}}\">x</a>");
            AddOverride(db, EmailTemplateKey.PasswordReset, EmailAudience.External, "EXT", "<a href=\"{{ResetLink}}\">x</a>");
            await db.SaveChangesAsync();

            var mail = new RecordingEmailSender();
            var svc = EmailTemplateServices.Real(db, mail, InternalExampleCom);

            await svc.SendAsync(EmailTemplateKey.PasswordReset, "guest@contractor.io", ResetTokens); // External row
            await svc.SendAsync(EmailTemplateKey.PasswordReset, "staff@example.com", ResetTokens);   // no Internal row -> Any

            Assert.Equal(new[] { "EXT", "ANY" }, mail.Sent.Select(s => s.Subject));
            Assert.Equal(EmailAudience.External, mail.Sent[0].Origin?.Audience);
            Assert.Equal(EmailAudience.Internal, mail.Sent[1].Origin?.Audience);
        });
    }

    [Fact]
    public async Task Custom_AppName_Flows_Into_Defaults()
    {
        await WithDb(async (db, _) =>
        {
            var mail = new RecordingEmailSender();
            var svc = EmailTemplateServices.Real(db, mail, new Dictionary<string, string?> { ["Email:AppName"] = "Acme Travel" });
            await svc.SendAsync(EmailTemplateKey.PasswordReset, "a@example.com", ResetTokens);

            Assert.Equal("Reset your Acme Travel password", Assert.Single(mail.Sent).Subject);
        });
    }

    // ---- Runtime fallback ---------------------------------------------------------

    [Fact]
    public async Task Corrupt_Stored_Template_Falls_Back_To_Default()
    {
        await WithDb(async (db, _) =>
        {
            // Drifted row: no reset link and a token that no longer exists.
            AddOverride(db, EmailTemplateKey.PasswordReset, EmailAudience.Any, "Broken {{Removed.Token}}", "<p>no link</p>");
            await db.SaveChangesAsync();

            var mail = new RecordingEmailSender();
            await EmailTemplateServices.Real(db, mail).SendAsync(EmailTemplateKey.PasswordReset, "a@example.com", ResetTokens);

            var sent = Assert.Single(mail.Sent);
            Assert.Equal("Reset your Travel Tracker password", sent.Subject);
            Assert.Contains("https://app/reset?token=T", sent.Body);
        });
    }

    // ---- Audit ------------------------------------------------------------------

    [Fact]
    public async Task NotificationLog_Records_Template_And_Audience_But_No_Body()
    {
        await WithDb(async (db, _) =>
        {
            var audited = new AuditingEmailSender(new RecordingEmailSender(), db, NullLogger<AuditingEmailSender>.Instance);
            await EmailTemplateServices.Real(db, audited, InternalExampleCom)
                .SendAsync(EmailTemplateKey.PasswordReset, "staff@example.com", ResetTokens);

            var log = await db.NotificationLogs.AsNoTracking().SingleAsync();
            Assert.Equal(EmailTemplateKey.PasswordReset, log.TemplateKey);
            Assert.Equal(EmailAudience.Internal, log.Audience);
            Assert.Equal("Reset your Travel Tracker password", log.Subject);
            // NotificationLog has no body column at all; the reset link must not leak into any stored field.
            Assert.DoesNotContain("token=T", log.Subject + log.Error + log.Recipient);
        });
    }

    // ---- Admin Edit page ----------------------------------------------------------

    [Fact]
    public async Task Edit_Save_Sanitizes_And_Upserts_Override()
    {
        await WithDb(async (db, sp) =>
        {
            var page = NewEditPage(sp, db, EmailTemplateKey.PasswordReset, EmailAudience.External);
            page.Input = new EditModel.InputModel
            {
                Subject = "  Reset for contractors  ",
                HtmlBody = "<p onclick=\"x()\">Hi</p><script>bad()</script><a href=\"{{ResetLink}}\">Reset</a>",
            };

            Assert.IsType<RedirectToPageResult>(await page.OnPostSaveAsync());

            var row = await db.EmailTemplates.AsNoTracking().SingleAsync();
            Assert.Equal(EmailAudience.External, row.Audience);
            Assert.Equal("Reset for contractors", row.Subject);
            Assert.DoesNotContain("script", row.HtmlBody);
            Assert.DoesNotContain("onclick", row.HtmlBody);
            Assert.Contains("href=\"{{ResetLink}}\"", row.HtmlBody);
            Assert.Equal(AdminId, row.UpdatedById);

            // Saving again updates the same row rather than adding a second one.
            var again = NewEditPage(sp, db, EmailTemplateKey.PasswordReset, EmailAudience.External);
            again.Input = new EditModel.InputModel { Subject = "v2", HtmlBody = "<a href=\"{{ResetLink}}\">Reset</a>" };
            await again.OnPostSaveAsync();
            Assert.Equal("v2", (await db.EmailTemplates.AsNoTracking().SingleAsync()).Subject);
        });
    }

    [Fact]
    public async Task Edit_Save_Rejects_Missing_Required_Token()
    {
        await WithDb(async (db, sp) =>
        {
            var page = NewEditPage(sp, db, EmailTemplateKey.Invitation, EmailAudience.Any);
            page.Input = new EditModel.InputModel { Subject = "Hi", HtmlBody = "<p>No link here</p>" };

            Assert.IsType<PageResult>(await page.OnPostSaveAsync());
            Assert.False(page.ModelState.IsValid);
            Assert.Equal(0, await db.EmailTemplates.CountAsync());
        });
    }

    [Fact]
    public async Task Edit_Reset_Deletes_Override()
    {
        await WithDb(async (db, sp) =>
        {
            AddOverride(db, EmailTemplateKey.TripApproved, EmailAudience.Internal, "s", "<p>b</p>");
            await db.SaveChangesAsync();

            var page = NewEditPage(sp, db, EmailTemplateKey.TripApproved, EmailAudience.Internal);
            Assert.IsType<RedirectToPageResult>(await page.OnPostResetAsync());
            Assert.Equal(0, await db.EmailTemplates.CountAsync());
        });
    }

    [Fact]
    public async Task Edit_Get_Prefills_From_Inherited_Template()
    {
        await WithDb(async (db, sp) =>
        {
            AddOverride(db, EmailTemplateKey.TripApproved, EmailAudience.Any, "Any subject", "<p>any</p>");
            await db.SaveChangesAsync();

            var page = NewEditPage(sp, db, EmailTemplateKey.TripApproved, EmailAudience.External);
            Assert.IsType<PageResult>(await page.OnGetAsync());

            Assert.Null(page.Existing);
            Assert.Equal("Any subject", page.Input.Subject);
            Assert.Equal("the Any override", page.InheritsFrom);
        });
    }

    // ---- Plumbing -------------------------------------------------------------------

    private static EditModel NewEditPage(IServiceProvider sp, AppDbContext db, EmailTemplateKey key, EmailAudience audience)
    {
        var http = new DefaultHttpContext
        {
            RequestServices = sp,
            User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.NameIdentifier, AdminId) }, "Test")),
        };
        var ac = new ActionContext(http, new RouteData(), new PageActionDescriptor(), new ModelStateDictionary());
        var config = sp.GetRequiredService<IConfiguration>();
        return new EditModel(db, EmailTemplateServices.Real(db, new RecordingEmailSender()),
            sp.GetRequiredService<UserManager<AppUser>>(), config, NullLogger<EditModel>.Instance)
        {
            PageContext = new PageContext(ac),
            TempData = new TempDataDictionary(http, new TestTempDataProvider()),
            Key = key,
            Audience = audience,
        };
    }

    private static async Task WithDb(Func<AppDbContext, IServiceProvider, Task> body)
    {
        var token = $"tt-templates-{Guid.NewGuid():N}";
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = TestDatabase.ConnectionStringFor(token),
            }).Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddLogging();
        services.AddDataProtection();
        services.AddAppDatabase(config);
        services.AddIdentityCore<AppUser>().AddRoles<IdentityRole>().AddEntityFrameworkStores<AppDbContext>();
        var sp = services.BuildServiceProvider();
        try
        {
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.EnsureCreatedAsync();
            // Target of EmailTemplate.UpdatedById (Restrict FK) and the page's signed-in admin.
            db.Users.Add(new AppUser { Id = AdminId, UserName = "admin@example.com", Email = "admin@example.com", DisplayName = "Admin" });
            await db.SaveChangesAsync();
            await body(db, scope.ServiceProvider);
        }
        finally
        {
            await sp.DisposeAsync();
            TestDatabase.Drop(TestDatabase.ConnectionStringFor(token));
        }
    }

    private sealed class TestTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
}
