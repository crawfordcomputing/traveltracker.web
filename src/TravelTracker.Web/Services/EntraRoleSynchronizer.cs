using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using TravelTracker.Web.Data;
using TravelTracker.Web.Data.Entities;
using TravelTracker.Web.Domain;

namespace TravelTracker.Web.Services;

// Applies the Entra group -> app-role JIT mapping (EntraRoleMapper) to a user's
// Identity roles at login. Active only when Auth:EnableEntraId=true AND
// Auth:EntraId:SyncRolesOnLogin=true. Defaults to OFF: the app is the source of
// truth for roles (Option A), so Entra is an authentication method only and never
// reconciles roles at login unless an operator explicitly opts in. Even when opted
// in, with no mappings configured it is a no-op.
//
// This is the I/O side of the feature (claims in, Identity roles out); all decision
// logic lives in the pure EntraRoleMapper so it can be unit-tested without a DB.
public sealed class EntraRoleSynchronizer
{
    private readonly UserManager<AppUser> _userManager;
    private readonly IConfiguration _config;
    private readonly ILogger<EntraRoleSynchronizer> _logger;

    public EntraRoleSynchronizer(
        UserManager<AppUser> userManager,
        IConfiguration config,
        ILogger<EntraRoleSynchronizer> logger)
    {
        _userManager = userManager;
        _config = config;
        _logger = logger;
    }

    public bool Enabled =>
        AuthSetup.IsEntraIdEnabled(_config)
        && (_config.GetValue<bool?>("Auth:EntraId:SyncRolesOnLogin") ?? false);

    // Reconcile `user`'s Identity roles against the security groups carried on
    // `principal`. Returns true when any role was added or removed. When something
    // changed the security stamp is bumped so the new roles ride the very next
    // request instead of waiting for the next login.
    public async Task<bool> SyncAsync(AppUser user, ClaimsPrincipal principal)
    {
        if (!Enabled) return false;

        IReadOnlySet<string> desired;
        IReadOnlySet<string> managed;

        if (_config.GetValue<bool>("Auth:EntraId:RoleClaimPassthrough"))
        {
            // App-role pass-through (recommended): Entra app-role values match our role
            // names, so we read the app-role claim directly — no mapping table. The
            // directory is authoritative for the whole role model (see PassthroughPlan).
            // App roles are app-scoped and small, so there is no token-overflow concern.
            //
            // Entra emits app roles in the "roles" claim, but ASP.NET Core's default
            // inbound claim mapping (MapInboundClaims=true) rewrites it to ClaimTypes.Role
            // (http://schemas.microsoft.com/ws/2008/06/identity/claims/role). Read both so
            // the sync is correct whether or not that mapping is enabled; PassthroughPlan
            // de-duplicates, so overlap is harmless.
            var roleValues = principal.FindAll("roles")
                .Concat(principal.FindAll(ClaimTypes.Role))
                .Select(c => c.Value)
                .ToList();

            // Diagnostics for the two ways a directory-side misconfiguration silently
            // lands a user on the Employee floor. Neither is fatal — sync continues.
            if (roleValues.Count == 0)
            {
                _logger.LogWarning(
                    "Entra sign-in for {User} carried no 'roles' claim; user keeps only the " +
                    "Employee floor. Confirm the user is assigned an app role in the Enterprise " +
                    "Application and that the app registration emits app roles.",
                    user.Email);
            }
            else
            {
                var unknown = EntraRoleMapper.UnknownRoleValues(roleValues);
                if (unknown.Count > 0)
                    _logger.LogWarning(
                        "Entra sign-in for {User} carried role value(s) [{Unknown}] that match no " +
                        "app role and were ignored. Known roles: [{Known}]. The app-role Value in " +
                        "Entra (not its Display name) must equal one of these.",
                        user.Email, string.Join(", ", unknown), string.Join(", ", Roles.All));
            }

            (desired, managed) = EntraRoleMapper.PassthroughPlan(roleValues);
        }
        else
        {
            var maps = EntraRoleMapper.ParseMappings(
                _config.GetSection("Auth:EntraId:GroupRoleMappings").Get<EntraGroupRoleMap[]>());
            if (maps.Count == 0) return false;

            // Entra emits security-group object ids in the "groups" claim by default;
            // allow the claim type to be overridden.
            var claimType = _config["Auth:EntraId:GroupClaimType"];
            if (string.IsNullOrWhiteSpace(claimType)) claimType = "groups";

            // Large directories overflow the token and Entra sends an overage pointer
            // (_claim_names / _claim_sources) instead of the group list, which needs a
            // Graph callback to resolve. Out of scope here; surface it so it is
            // diagnosable rather than silently granting nothing. (App-role pass-through
            // sidesteps this entirely.)
            if (claimType == "groups"
                && principal.FindFirst("_claim_names") is not null
                && !principal.FindAll(claimType).Any())
            {
                _logger.LogWarning(
                    "Entra group claim for {User} overflowed the token (overage claim present); " +
                    "role sync skipped. Reduce emitted groups, switch to app-role pass-through, " +
                    "or resolve via Microsoft Graph.",
                    user.Email);
                return false;
            }

            var groupIds = principal.FindAll(claimType).Select(c => c.Value);
            desired = EntraRoleMapper.DesiredRoles(groupIds, maps);
            managed = EntraRoleMapper.ManagedRoles(maps);
        }

        var current = await _userManager.GetRolesAsync(user);
        var (toAdd, toRemove) = EntraRoleMapper.Reconcile(current, desired, managed);

        // A misconfigured group must never be able to lock everyone out: never strip
        // Admin from the last active administrator.
        if (toRemove.Contains(Roles.Admin)
            && await AdminGuard.IsLastActiveAdminAsync(_userManager, user))
        {
            _logger.LogWarning(
                "Entra role sync would remove Admin from the last active admin ({User}); kept.",
                user.Email);
            toRemove = toRemove.Where(r => r != Roles.Admin).ToList();
        }

        var changed = false;
        if (toAdd.Count > 0)
            changed |= (await _userManager.AddToRolesAsync(user, toAdd)).Succeeded;
        if (toRemove.Count > 0)
            changed |= (await _userManager.RemoveFromRolesAsync(user, toRemove)).Succeeded;

        if (changed)
        {
            await _userManager.UpdateSecurityStampAsync(user);
            _logger.LogInformation(
                "Entra role sync for {User}: added [{Added}], removed [{Removed}].",
                user.Email, string.Join(", ", toAdd), string.Join(", ", toRemove));
        }

        return changed;
    }
}
