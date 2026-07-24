using Microsoft.AspNetCore.Identity;
using TravelTracker.Web.Data.Entities;

namespace TravelTracker.Web.Auth;

// Enforces the Auth:Mfa:RequireForAdmins toggle: an authenticated Admin who has not yet
// enrolled a TOTP authenticator is corralled to the enrollment page. They can still reach
// /Account/* (to enroll or log out) and /healthz — everything else redirects to setup
// until they enable 2FA. Registered only when the toggle is on (see Program.cs), so the
// per-request UserManager lookup never runs otherwise. Static assets are served by
// UseStaticFiles earlier in the pipeline and never reach here.
public class MfaEnforcementMiddleware
{
    public const string EnrollPath = "/Account/Manage/EnableAuthenticator";
    private readonly RequestDelegate _next;

    public MfaEnforcementMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, UserManager<AppUser> userManager)
    {
        if (ShouldGate(context))
        {
            var user = await userManager.GetUserAsync(context.User);
            if (user is not null && !await userManager.GetTwoFactorEnabledAsync(user))
            {
                context.Response.Redirect(EnrollPath);
                return;
            }
        }

        await _next(context);
    }

    private static bool ShouldGate(HttpContext context)
    {
        // Claim-based checks first — no DB work unless this is an authenticated Admin on a
        // page outside the always-allowed account/health surface.
        if (context.User.Identity?.IsAuthenticated != true) return false;
        if (!context.User.IsInRole(Roles.Admin)) return false;

        var path = context.Request.Path;
        if (path.StartsWithSegments("/Account")) return false;
        if (path.StartsWithSegments("/healthz")) return false;
        return true;
    }
}
