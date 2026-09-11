using Microsoft.AspNetCore.Authentication;
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
using TravelTracker.Web.Pages.Account;
using TravelTracker.Web.Services;
using Xunit;

namespace TravelTracker.Tests;

// Drives the real Account/Register handlers across every registration mode and
// both the self-serve and invite-redemption paths, including the sign-in success
// tails (full Identity cookie auth is wired so SignInAsync executes).
public class RegisterTests
{
    private static ServiceProvider BuildServices(string dbFile, Dictionary<string, string?> extraConfig)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = TestDatabase.ConnectionStringFor(dbFile)
        };
        foreach (var kv in extraConfig) settings[kv.Key] = kv.Value;

        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddLogging();
        services.AddDataProtection();
        services.AddHttpContextAccessor();
        services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
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

    private static RegisterModel NewPage(IServiceProvider sp, out DefaultHttpContext http)
    {
        http = new DefaultHttpContext { RequestServices = sp };
        // SignInManager resolves its HttpContext via IHttpContextAccessor; wire it so
        // SignInAsync can run on the success paths.
        sp.GetRequiredService<IHttpContextAccessor>().HttpContext = http;
        var ac = new ActionContext(http, new RouteData(), new PageActionDescriptor(), new ModelStateDictionary());
        var db = sp.GetRequiredService<AppDbContext>();
        var confirmations = new EmailConfirmationService(
            sp.GetRequiredService<UserManager<AppUser>>(), new NoopEmailTemplateService());
        return new RegisterModel(
            sp.GetRequiredService<UserManager<AppUser>>(),
            sp.GetRequiredService<SignInManager<AppUser>>(),
            sp.GetRequiredService<IConfiguration>(),
            db,
            confirmations)
        {
            PageContext = new PageContext(ac),
            TempData = new TempDataDictionary(http, new TestTempDataProvider()),
            Url = new FakeUrlHelper(ac)
        };
    }

    private static async Task<Invitation> SeedInvite(
        AppDbContext db, string email, string rawToken, string role = "Employee", int days = 7)
    {
        var invite = new Invitation
        {
            Email = email,
            Role = role,
            TokenHash = InviteTokens.Hash(rawToken),
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(days)
        };
        db.Invitations.Add(invite);
        await db.SaveChangesAsync();
        return invite;
    }

    // ---- OnGet ------------------------------------------------------------

    [Fact]
    public async Task OnGet_SelfServe_Open_Shows_Form()
    {
        await WithScope(Open, async (sp, db, um) =>
        {
            var page = NewPage(sp, out _);
            Assert.IsType<PageResult>(await page.OnGetAsync());
            Assert.True(page.RegistrationAvailable);
            Assert.False(page.IsInvite);
        });
    }

    [Fact]
    public async Task OnGet_Closed_Without_Token_Hides_Form()
    {
        await WithScope(Closed, async (sp, db, um) =>
        {
            var page = NewPage(sp, out _);
            Assert.IsType<PageResult>(await page.OnGetAsync());
            Assert.False(page.RegistrationAvailable);
        });
    }

    [Fact]
    public async Task OnGet_Valid_Token_Enters_Invite_Mode()
    {
        await WithScope(Invite, async (sp, db, um) =>
        {
            await SeedInvite(db, "invited@example.com", "tok-get");
            var page = NewPage(sp, out _);
            page.Token = "tok-get";

            Assert.IsType<PageResult>(await page.OnGetAsync());
            Assert.True(page.IsInvite);
            Assert.Equal("invited@example.com", page.Input.Email);
        });
    }

    [Fact]
    public async Task OnGet_Invalid_Token_Marks_Unavailable()
    {
        await WithScope(Invite, async (sp, db, um) =>
        {
            var page = NewPage(sp, out _);
            page.Token = "does-not-exist";

            Assert.IsType<PageResult>(await page.OnGetAsync());
            Assert.False(page.RegistrationAvailable);
            Assert.False(page.IsInvite);
        });
    }

    // ---- OnPost self-serve ------------------------------------------------

    [Fact]
    public async Task OnPost_SelfServe_Disabled_Is_Forbidden()
    {
        await WithScope(Invite, async (sp, db, um) =>
        {
            var page = NewPage(sp, out _);
            page.Input = ValidInput("nope@example.com");
            Assert.IsType<ForbidResult>(await page.OnPostAsync("/"));
        });
    }

    [Fact]
    public async Task OnPost_SelfServe_Domain_Blocks_Unapproved_Email()
    {
        await WithScope(Domain("company.com"), async (sp, db, um) =>
        {
            var page = NewPage(sp, out _);
            page.Input = ValidInput("outsider@gmail.com");

            Assert.IsType<PageResult>(await page.OnPostAsync("/"));
            Assert.True(page.ModelState.ContainsKey("Input.Email"));
            Assert.Equal(0, await db.Users.CountAsync());
        });
    }

    [Fact]
    public async Task OnPost_SelfServe_Invalid_ModelState_Returns_Page()
    {
        await WithScope(Open, async (sp, db, um) =>
        {
            var page = NewPage(sp, out _);
            page.Input = ValidInput("x@example.com");
            page.ModelState.AddModelError("Input.Password", "too weak"); // simulate bound-model failure

            Assert.IsType<PageResult>(await page.OnPostAsync("/"));
            Assert.Equal(0, await db.Users.CountAsync());
        });
    }

    [Fact]
    public async Task OnPost_SelfServe_Open_Creates_Employee_And_SignsIn()
    {
        await WithScope(Open, async (sp, db, um) =>
        {
            var page = NewPage(sp, out _);
            page.Input = ValidInput("self@example.com");

            var result = await page.OnPostAsync("/");
            Assert.IsType<LocalRedirectResult>(result);

            var user = await um.FindByEmailAsync("self@example.com");
            Assert.NotNull(user);
            Assert.True(await um.IsInRoleAsync(user!, Roles.Employee));
        });
    }

    // ---- OnPost invite ----------------------------------------------------

    [Fact]
    public async Task OnPost_Invite_Password_Mismatch_Returns_Page()
    {
        await WithScope(Invite, async (sp, db, um) =>
        {
            await SeedInvite(db, "m@example.com", "tok-mismatch");
            var page = NewPage(sp, out _);
            page.Token = "tok-mismatch";
            page.Input = new RegisterModel.InputModel
            {
                DisplayName = "M", Email = "ignored@evil.com",
                Password = "Passw0rd!", ConfirmPassword = "Different1!"
            };

            Assert.IsType<PageResult>(await page.OnPostAsync("/"));
            Assert.False(page.ModelState.IsValid);
            Assert.Equal(0, await db.Users.CountAsync());
        });
    }

    [Fact]
    public async Task OnPost_Invite_Invalid_Token_Marks_Unavailable()
    {
        await WithScope(Invite, async (sp, db, um) =>
        {
            var page = NewPage(sp, out _);
            page.Token = "bogus";
            page.Input = ValidInput("whoever@example.com");

            Assert.IsType<PageResult>(await page.OnPostAsync("/"));
            Assert.False(page.RegistrationAvailable);
        });
    }

    [Fact]
    public async Task OnPost_Invite_Success_Creates_User_Spends_Invite_And_SignsIn()
    {
        await WithScope(Invite, async (sp, db, um) =>
        {
            var invite = await SeedInvite(db, "grant@example.com", "tok-ok", Roles.Manager);
            var page = NewPage(sp, out _);
            page.Token = "tok-ok";
            page.Input = new RegisterModel.InputModel
            {
                DisplayName = "Granted", Email = "tampered@evil.com", // must be ignored
                Password = "Passw0rd!", ConfirmPassword = "Passw0rd!"
            };

            var result = await page.OnPostAsync("/");
            Assert.IsType<LocalRedirectResult>(result);

            var user = await um.FindByEmailAsync("grant@example.com");
            Assert.NotNull(user);
            Assert.True(await um.IsInRoleAsync(user!, Roles.Manager)); // role from invite, not self-chosen

            db.ChangeTracker.Clear();
            var spent = await db.Invitations.FindAsync(invite.Id);
            Assert.NotNull(spent!.AcceptedAt); // invite consumed
        });
    }

    // ---- helpers ----------------------------------------------------------

    private static RegisterModel.InputModel ValidInput(string email) => new()
    {
        DisplayName = "Test User",
        Email = email,
        Password = "Passw0rd!",
        ConfirmPassword = "Passw0rd!"
    };

    private static Dictionary<string, string?> Open => new() { ["Auth:Registration:Mode"] = "Open" };
    private static Dictionary<string, string?> Closed => new() { ["Auth:Registration:Mode"] = "Closed" };
    private static Dictionary<string, string?> Invite => new() { ["Auth:Registration:Mode"] = "Invite" };
    private static Dictionary<string, string?> Domain(string domain) => new()
    {
        ["Auth:Registration:Mode"] = "Domain",
        ["Auth:Registration:AllowedDomains:0"] = domain
    };

    private static async Task WithScope(
        Dictionary<string, string?> config,
        Func<IServiceProvider, AppDbContext, UserManager<AppUser>, Task> body)
    {
        var dbFile = Path.Combine(Path.GetTempPath(), $"tt-register-{Guid.NewGuid():N}.db");
        var sp = BuildServices(dbFile, config);
        try
        {
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var um = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var rm = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            await db.Database.EnsureCreatedAsync();
            foreach (var r in Roles.All) await rm.CreateAsync(new IdentityRole(r));
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
