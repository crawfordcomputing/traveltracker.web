using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TravelTracker.Web.Data;
using TravelTracker.Web.Data.Entities;
using TravelTracker.Web.Pages.Account;
using TravelTracker.Web.Services;
using Xunit;

namespace TravelTracker.Tests;

// Drives the real ForgotPassword + ResetPassword page handlers end to end: a reset
// link is emailed only for a valid active account, and redeeming its token lets the
// user authenticate with the new password.
public class PasswordResetTests
{
    private static ServiceProvider BuildServices(string dbFile, CapturingEmailSender email)
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
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders();
        services.AddSingleton<IEmailSender>(email);
        return services.BuildServiceProvider();
    }

    private static void Cleanup(string dbFile)
    {
        TestDatabase.Drop(TestDatabase.ConnectionStringFor(dbFile));
    }

    private static async Task<AppUser> CreateUserAsync(UserManager<AppUser> um, string email)
    {
        var user = new AppUser { UserName = email, Email = email, DisplayName = email };
        var result = await um.CreateAsync(user, "OldPassw0rd!");
        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(e => e.Description)));
        return user;
    }

    private static PageContext NewPageContext(IServiceProvider sp)
    {
        var httpContext = new DefaultHttpContext { RequestServices = sp };
        var actionContext = new ActionContext(
            httpContext, new RouteData(), new PageActionDescriptor(), new ModelStateDictionary());
        return new PageContext(actionContext);
    }

    [Fact]
    public async Task Forgot_then_Reset_lets_user_sign_in_with_new_password()
    {
        var dbFile = Path.Combine(Path.GetTempPath(), $"tt-reset-{Guid.NewGuid():N}.db");
        var email = new CapturingEmailSender();
        var sp = BuildServices(dbFile, email);
        try
        {
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var um = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            await db.Database.EnsureCreatedAsync();
            var user = await CreateUserAsync(um, "reset@example.com");

            // ---- Forgot: request a reset link -------------------------------
            var forgot = new ForgotPasswordModel(um, EmailTemplateServices.Real(db, email))
            {
                PageContext = NewPageContext(scope.ServiceProvider),
                Url = new FakeUrlHelper(),
                Input = new ForgotPasswordModel.InputModel { Email = "reset@example.com" }
            };
            var forgotResult = await forgot.OnPostAsync();

            Assert.IsType<PageResult>(forgotResult);
            Assert.True(forgot.Submitted);
            var sent = Assert.Single(email.Sent);
            var token = Regex.Match(sent.Body, "token=([^\"&]+)").Groups[1].Value;
            Assert.False(string.IsNullOrEmpty(token));

            // ---- Reset: redeem the token ------------------------------------
            var reset = new ResetPasswordModel(um)
            {
                PageContext = NewPageContext(scope.ServiceProvider),
                Input = new ResetPasswordModel.InputModel
                {
                    Email = "reset@example.com",
                    Token = token,
                    Password = "BrandNew1!",
                    ConfirmPassword = "BrandNew1!"
                }
            };
            var resetResult = await reset.OnPostAsync();

            Assert.IsType<RedirectToPageResult>(resetResult);
            var reloaded = await um.FindByEmailAsync("reset@example.com");
            Assert.True(await um.CheckPasswordAsync(reloaded!, "BrandNew1!"));
            Assert.False(await um.CheckPasswordAsync(reloaded!, "OldPassw0rd!"));
        }
        finally { await sp.DisposeAsync(); Cleanup(dbFile); }
    }

    [Fact]
    public async Task Forgot_does_not_email_unknown_address()
    {
        var dbFile = Path.Combine(Path.GetTempPath(), $"tt-reset-{Guid.NewGuid():N}.db");
        var email = new CapturingEmailSender();
        var sp = BuildServices(dbFile, email);
        try
        {
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var um = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            await db.Database.EnsureCreatedAsync();

            var forgot = new ForgotPasswordModel(um, EmailTemplateServices.Real(db, email))
            {
                PageContext = NewPageContext(scope.ServiceProvider),
                Url = new FakeUrlHelper(),
                Input = new ForgotPasswordModel.InputModel { Email = "nobody@example.com" }
            };
            var result = await forgot.OnPostAsync();

            // Neutral outcome (same page + Submitted) but no mail leaves the building.
            Assert.IsType<PageResult>(result);
            Assert.True(forgot.Submitted);
            Assert.Empty(email.Sent);
        }
        finally { await sp.DisposeAsync(); Cleanup(dbFile); }
    }

    [Fact]
    public async Task Forgot_skips_deactivated_account()
    {
        var dbFile = Path.Combine(Path.GetTempPath(), $"tt-reset-{Guid.NewGuid():N}.db");
        var email = new CapturingEmailSender();
        var sp = BuildServices(dbFile, email);
        try
        {
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var um = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            await db.Database.EnsureCreatedAsync();
            var user = await CreateUserAsync(um, "leaver@example.com");
            user.IsActive = false;
            await um.UpdateAsync(user);

            var forgot = new ForgotPasswordModel(um, EmailTemplateServices.Real(db, email))
            {
                PageContext = NewPageContext(scope.ServiceProvider),
                Url = new FakeUrlHelper(),
                Input = new ForgotPasswordModel.InputModel { Email = "leaver@example.com" }
            };
            await forgot.OnPostAsync();

            Assert.Empty(email.Sent);
        }
        finally { await sp.DisposeAsync(); Cleanup(dbFile); }
    }

    [Fact]
    public async Task Reset_with_tampered_token_fails()
    {
        var dbFile = Path.Combine(Path.GetTempPath(), $"tt-reset-{Guid.NewGuid():N}.db");
        var email = new CapturingEmailSender();
        var sp = BuildServices(dbFile, email);
        try
        {
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var um = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            await db.Database.EnsureCreatedAsync();
            await CreateUserAsync(um, "reset@example.com");

            var reset = new ResetPasswordModel(um)
            {
                PageContext = NewPageContext(scope.ServiceProvider),
                Input = new ResetPasswordModel.InputModel
                {
                    Email = "reset@example.com",
                    Token = "bm90LWEtcmVhbC10b2tlbg", // base64url but not a valid reset token
                    Password = "BrandNew1!",
                    ConfirmPassword = "BrandNew1!"
                }
            };
            var result = await reset.OnPostAsync();

            Assert.IsType<PageResult>(result);
            Assert.False(reset.ModelState.IsValid);
            var reloaded = await um.FindByEmailAsync("reset@example.com");
            Assert.False(await um.CheckPasswordAsync(reloaded!, "BrandNew1!"));
        }
        finally { await sp.DisposeAsync(); Cleanup(dbFile); }
    }

    // Records outbound mail so the test can pull the reset link back out.
    private sealed class CapturingEmailSender : IEmailSender
    {
        public List<(string To, string Subject, string Body)> Sent { get; } = new();
        public Task SendAsync(string to, string subject, string htmlBody)
        {
            Sent.Add((to, subject, htmlBody));
            return Task.CompletedTask;
        }
    }

    // Url.Page(...) ultimately calls IUrlHelper.RouteUrl; that's the only member the
    // page exercises, so the rest can stay unimplemented.
    private sealed class FakeUrlHelper : IUrlHelper
    {
        // A fully-formed ActionContext: UrlHelperExtensions.Page reads RouteData off
        // it to resolve the page path before delegating to RouteUrl.
        public ActionContext ActionContext { get; } =
            new(new DefaultHttpContext(), new RouteData(), new PageActionDescriptor());
        public string? Action(UrlActionContext context) => string.Empty;
        public string? Content(string? contentPath) => contentPath;
        public bool IsLocalUrl(string? url) => true;
        public string? Link(string? routeName, object? values) => string.Empty;

        public string? RouteUrl(UrlRouteContext context)
        {
            var values = new RouteValueDictionary(context.Values);
            var page = values.TryGetValue("page", out var p) ? p?.ToString() : string.Empty;
            var query = string.Join("&", values
                .Where(kv => kv.Key is not ("page" or "handler" or "area"))
                .Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value?.ToString() ?? string.Empty)}"));
            var scheme = context.Protocol ?? "http";
            return $"{scheme}://localhost{page}" + (query.Length > 0 ? "?" + query : string.Empty);
        }
    }
}
