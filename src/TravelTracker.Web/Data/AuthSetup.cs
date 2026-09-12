using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using TravelTracker.Web.Auth;
using TravelTracker.Web.Data.Entities;
using TravelTracker.Web.Domain;

namespace TravelTracker.Web.Data;

// Auth wiring: ASP.NET Core Identity (local accounts) is always on. Microsoft
// Entra ID is an optional layer enabled with Auth:EnableEntraId=true — same
// "sensible default, one config switch" pattern as the DB provider.
public static class AuthSetup
{
    // Entra ID counts as on only when the switch is set AND all three credentials
    // are actually present. The switch ships true in appsettings.json while the
    // credentials are supplied out-of-band — user-secrets locally, app settings /
    // Key Vault in Azure — so "switch on, credentials absent" is the normal state
    // of a fresh clone, a CI run and the WebApplicationFactory test host (which
    // loads appsettings.json but no user secrets, since the entry assembly there
    // is the test runner). Registering the handler in that state makes
    // OpenIdConnectOptions.Validate() throw ArgumentException("ClientId") on every
    // request that passes through the authentication middleware, not just at
    // sign-in. So the credentials gate registration, and every surface that offers
    // an Entra button asks this same question rather than reading the switch alone
    // and advertising a scheme that was never registered.
    public static bool IsEntraIdEnabled(IConfiguration config) =>
        config.GetValue<bool>("Auth:EnableEntraId")
        && !string.IsNullOrWhiteSpace(config["Auth:EntraId:TenantId"])
        && !string.IsNullOrWhiteSpace(config["Auth:EntraId:ClientId"])
        && !string.IsNullOrWhiteSpace(config["Auth:EntraId:ClientSecret"]);

    public static IServiceCollection AddAppIdentity(
        this IServiceCollection services, IConfiguration config)
    {
        services.AddIdentity<AppUser, IdentityRole>(options =>
            {
                // Relaxed defaults so local eval is frictionless; tighten for prod.
                options.Password.RequiredLength = 8;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequireUppercase = false;
                // Off by default so local eval / invite flows stay frictionless;
                // prod turns it on with Auth:RequireConfirmedEmail=true. When on,
                // PasswordSignInAsync returns IsNotAllowed for unconfirmed accounts
                // (handled on the Login page) and self-serve registration emails a
                // confirmation link instead of signing in.
                options.SignIn.RequireConfirmedAccount =
                    config.GetValue<bool>("Auth:RequireConfirmedEmail");
                options.User.RequireUniqueEmail = true;

                // Password links (forgot-password, admin account setup, admin reset)
                // come from our own provider so their lifetime is explicit and
                // independent of email-confirmation tokens.
                options.Tokens.PasswordResetTokenProvider = PasswordSetupTokenProvider.ProviderName;
            })
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders()
            .AddTokenProvider<PasswordSetupTokenProvider>(PasswordSetupTokenProvider.ProviderName)
            // Custom SignInManager blocks deactivated (IsActive=false) accounts.
            .AddSignInManager<AppSignInManager>();

        // How long a password link stays usable. Explicit rather than inherited from
        // Identity's 1-day default, so an org can shorten it without touching the
        // email-confirmation tokens. Admin-issued credentials therefore expire on
        // their own (previously an admin's temporary password lived forever).
        services.Configure<PasswordSetupTokenProviderOptions>(o => o.TokenLifespan =
            TimeSpan.FromHours(config.GetValue<int?>("Auth:PasswordSetup:LifetimeHours") ?? 24));

        // Session hardening: travelers sign in on shared/hotel machines, so bound both an
        // idle timeout and an absolute cap. Idle timeout = ExpireTimeSpan + sliding renewal;
        // the absolute cap is enforced in OnValidatePrincipal below since sliding renewal
        // would otherwise let a session live forever.
        var idleTimeout = TimeSpan.FromMinutes(
            config.GetValue<int?>("Auth:Session:IdleTimeoutMinutes") ?? 30);
        var absoluteLifetime = TimeSpan.FromHours(
            config.GetValue<int?>("Auth:Session:AbsoluteExpiryHours") ?? 8);

        services.ConfigureApplicationCookie(options =>
        {
            options.LoginPath = "/Account/Login";
            options.LogoutPath = "/Account/Logout";
            options.AccessDeniedPath = "/Account/AccessDenied";

            // Idle timeout: the cookie expires after this much inactivity, renewed on
            // requests past the halfway mark (SlidingExpiration).
            options.ExpireTimeSpan = idleTimeout;
            options.SlidingExpiration = true;

            // Defensive cookie flags for shared machines / hostile networks.
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;

            // Stamp the original sign-in instant once. It rides along in the auth
            // properties through every sliding renewal (which only resets IssuedUtc), so
            // the absolute cap is measured from first login, not last activity.
            options.Events.OnSigningIn = ctx =>
            {
                ctx.Properties.Items[SessionPolicy.AbsoluteStartKey] =
                    DateTimeOffset.UtcNow.ToString("o");
                return Task.CompletedTask;
            };

            // OnValidatePrincipal runs on every authenticated request. Enforce the absolute
            // cap first, then chain to Identity's security-stamp validator — overriding this
            // event replaces the default handler, so we must call it explicitly or lose the
            // deactivation kick (AppSignInManager + shortened ValidationInterval).
            options.Events.OnValidatePrincipal = async ctx =>
            {
                var start = ctx.Properties.Items.TryGetValue(
                        SessionPolicy.AbsoluteStartKey, out var raw)
                        && DateTimeOffset.TryParse(raw, out var parsed)
                    ? parsed : (DateTimeOffset?)null;

                if (SessionPolicy.IsAbsolutelyExpired(start, DateTimeOffset.UtcNow, absoluteLifetime))
                {
                    ctx.RejectPrincipal();
                    await ctx.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
                    return;
                }

                await SecurityStampValidator.ValidatePrincipalAsync(ctx);
            };
        });

        // Re-check the security stamp every minute (default 30) so deactivating a
        // user — which also bumps their stamp — kicks their live cookie promptly.
        services.Configure<SecurityStampValidatorOptions>(o =>
            o.ValidationInterval = TimeSpan.FromMinutes(1));

        if (IsEntraIdEnabled(config))
        {
            services.AddAuthentication()
                .AddOpenIdConnect("EntraId", "Microsoft Entra ID", options =>
                {
                    options.Authority =
                        $"{config["Auth:EntraId:Instance"]?.TrimEnd('/')}/{config["Auth:EntraId:TenantId"]}/v2.0";
                    options.ClientId = config["Auth:EntraId:ClientId"];
                    options.ClientSecret = config["Auth:EntraId:ClientSecret"];
                    options.ResponseType = "code";
                    options.CallbackPath = "/signin-oidc";
                    options.SignInScheme = IdentityConstants.ExternalScheme;
                    options.Scope.Add("email");
                    options.Scope.Add("profile");
                    // Group->role JIT sync (EntraRoleSynchronizer) reads the "groups"
                    // claim on the id_token. Emitting it is an app-registration setting
                    // (Token configuration -> add groups claim, or manifest
                    // groupMembershipClaims), so nothing extra is requested here; the
                    // claim simply flows into the principal when the tenant emits it.
                });
        }

        services.AddAuthorization(options =>
        {
            options.AddPolicy("RequireAdmin", p => p.RequireRole(Roles.Admin));
            options.AddPolicy("RequireManager", p => p.RequireRole(Roles.Manager, Roles.Admin));
            // Finance can see all expenses/reports (M3/M4) without account god-mode.
            options.AddPolicy("RequireFinance", p => p.RequireRole(Roles.Finance, Roles.Admin));
            // M4 reporting surface: Finance/Admin see org-wide, Manager/Arranger see a
            // scoped view (ReportAccess decides the data scope). Plain Employees are
            // kept off the pages entirely — their own spend is on Trips/Details.
            options.AddPolicy("RequireReports",
                p => p.RequireRole(Roles.Finance, Roles.Manager, Roles.Arranger, Roles.Admin));
            // M5 reference-data management. Cost centers are a finance concern;
            // project codes are also a manager concern (they own their team's work).
            options.AddPolicy("RequireCostCenters",
                p => p.RequireRole(Roles.Finance, Roles.Admin));
            options.AddPolicy("RequireProjectCodes",
                p => p.RequireRole(Roles.Finance, Roles.Manager, Roles.Admin));
        });

        return services;
    }
}
