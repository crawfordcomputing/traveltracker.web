using System.Security.Cryptography;
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
using TravelTracker.Web.Pages.Account.Manage;
using Xunit;

namespace TravelTracker.Tests;

// Drives the TOTP enrollment page against real Identity: a valid authenticator code turns
// 2FA on and mints recovery codes; a wrong code is refused; and once enabled, a password
// sign-in demands the second factor.
public class TwoFactorTests
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
        services.AddAppDatabase(config);
        services.AddIdentity<AppUser, IdentityRole>(o =>
            {
                o.Password.RequiredLength = 8;
                o.Password.RequireNonAlphanumeric = false;
                o.Password.RequireUppercase = false;
                o.SignIn.RequireConfirmedAccount = false;
                o.User.RequireUniqueEmail = true;
            })
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders();

        return services.BuildServiceProvider();
    }

    private static void Cleanup(string dbFile)
    {
        TestDatabase.Drop(TestDatabase.ConnectionStringFor(dbFile));
    }

    private static async Task<EnableAuthenticatorModel> NewEnablePageAsync(
        IServiceProvider sp, AppUser user)
    {
        var um = sp.GetRequiredService<UserManager<AppUser>>();
        var signIn = sp.GetRequiredService<SignInManager<AppUser>>();
        var principal = await signIn.CreateUserPrincipalAsync(user);
        var httpContext = new DefaultHttpContext { RequestServices = sp, User = principal };
        var actionContext = new ActionContext(
            httpContext, new RouteData(), new PageActionDescriptor(), new ModelStateDictionary());
        return new EnableAuthenticatorModel(um)
        {
            PageContext = new PageContext(actionContext),
            TempData = new TempDataDictionary(httpContext, new TestTempDataProvider())
        };
    }

    [Fact]
    public async Task Valid_code_enables_2fa_and_mints_recovery_codes()
    {
        var dbFile = Path.Combine(Path.GetTempPath(), $"tt-2fa-{Guid.NewGuid():N}.db");
        var sp = BuildServices(dbFile);
        try
        {
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var um = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            await db.Database.EnsureCreatedAsync();

            var user = new AppUser { UserName = "u@example.com", Email = "u@example.com", DisplayName = "U" };
            await um.CreateAsync(user, "Passw0rd!");

            var page = await NewEnablePageAsync(scope.ServiceProvider, user);
            await page.OnGetAsync(); // establishes the authenticator key

            // Identity's authenticator provider never generates codes (the app does), so
            // compute a valid current TOTP from the Base32 shared key ourselves.
            var key = await um.GetAuthenticatorKeyAsync(user);
            var code = Totp.Now(key!);
            page.Input = new EnableAuthenticatorModel.InputModel { Code = code };
            var result = await page.OnPostAsync();

            Assert.IsType<RedirectToPageResult>(result);
            Assert.Equal("./ShowRecoveryCodes", ((RedirectToPageResult)result).PageName);
            var reloaded = await um.FindByIdAsync(user.Id);
            Assert.True(await um.GetTwoFactorEnabledAsync(reloaded!));
            Assert.Equal(10, await um.CountRecoveryCodesAsync(reloaded!));
            var codes = Assert.IsType<string[]>(page.TempData["RecoveryCodes"]);
            Assert.Equal(10, codes.Length);
        }
        finally { await sp.DisposeAsync(); Cleanup(dbFile); }
    }

    [Fact]
    public async Task Wrong_code_is_rejected_and_2fa_stays_off()
    {
        var dbFile = Path.Combine(Path.GetTempPath(), $"tt-2fa-{Guid.NewGuid():N}.db");
        var sp = BuildServices(dbFile);
        try
        {
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var um = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            await db.Database.EnsureCreatedAsync();

            var user = new AppUser { UserName = "u@example.com", Email = "u@example.com", DisplayName = "U" };
            await um.CreateAsync(user, "Passw0rd!");

            var page = await NewEnablePageAsync(scope.ServiceProvider, user);
            await page.OnGetAsync();
            page.Input = new EnableAuthenticatorModel.InputModel { Code = "000000" };
            var result = await page.OnPostAsync();

            Assert.IsType<PageResult>(result);
            Assert.False(page.ModelState.IsValid);
            var reloaded = await um.FindByIdAsync(user.Id);
            Assert.False(await um.GetTwoFactorEnabledAsync(reloaded!));
        }
        finally { await sp.DisposeAsync(); Cleanup(dbFile); }
    }

    [Fact]
    public async Task Password_sign_in_requires_second_factor_once_enabled()
    {
        var dbFile = Path.Combine(Path.GetTempPath(), $"tt-2fa-{Guid.NewGuid():N}.db");
        var sp = BuildServices(dbFile);
        try
        {
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var um = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var signIn = scope.ServiceProvider.GetRequiredService<SignInManager<AppUser>>();
            await db.Database.EnsureCreatedAsync();

            var user = new AppUser { UserName = "u@example.com", Email = "u@example.com", DisplayName = "U" };
            await um.CreateAsync(user, "Passw0rd!");
            // An authenticator key makes the Authenticator a *valid* provider — without one,
            // SignInManager sees no usable second factor and completes the sign-in outright.
            await um.ResetAuthenticatorKeyAsync(user);
            await um.SetTwoFactorEnabledAsync(user, true);

            // PasswordSignInAsync performs the partial two-factor sign-in against the
            // HttpContext, so give the SignInManager a context wired to these services.
            var httpContext = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
            scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = httpContext;

            var result = await signIn.PasswordSignInAsync(
                user, "Passw0rd!", isPersistent: false, lockoutOnFailure: false);

            Assert.True(result.RequiresTwoFactor);
            Assert.False(result.Succeeded);
        }
        finally { await sp.DisposeAsync(); Cleanup(dbFile); }
    }

    private sealed class TestTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) =>
            new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    // Minimal RFC-6238 TOTP (30s step, 6 digits, HMAC-SHA1) over a Base32 secret — the
    // standard Google Authenticator algorithm that Identity's authenticator provider
    // validates against. Enough to synthesise a code an authenticator app would show.
    private static class Totp
    {
        private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

        public static string Now(string base32Key)
        {
            var key = FromBase32(base32Key);
            var counter = (long)((DateTimeOffset.UtcNow - DateTimeOffset.UnixEpoch).TotalSeconds / 30);

            var counterBytes = BitConverter.GetBytes(counter);
            if (BitConverter.IsLittleEndian) System.Array.Reverse(counterBytes);

            using var hmac = new HMACSHA1(key);
            var hash = hmac.ComputeHash(counterBytes);
            var offset = hash[^1] & 0x0f;
            var binary = ((hash[offset] & 0x7f) << 24)
                       | ((hash[offset + 1] & 0xff) << 16)
                       | ((hash[offset + 2] & 0xff) << 8)
                       | (hash[offset + 3] & 0xff);
            return (binary % 1_000_000).ToString("D6");
        }

        private static byte[] FromBase32(string input)
        {
            input = input.TrimEnd('=').ToUpperInvariant();
            var bits = 0;
            var value = 0;
            var output = new List<byte>();
            foreach (var c in input)
            {
                value = (value << 5) | Base32Alphabet.IndexOf(c);
                bits += 5;
                if (bits >= 8)
                {
                    output.Add((byte)((value >> (bits - 8)) & 0xff));
                    bits -= 8;
                }
            }
            return output.ToArray();
        }
    }
}
