using TravelTracker.Web.Data.Entities;

namespace TravelTracker.Web.Domain;

// One configured Entra security-group -> app-role mapping. Many-to-many is allowed:
// a group may grant several roles, and several groups may grant the same role.
public sealed record EntraGroupRoleMap(string GroupId, string Role);

// Maps Entra ID security-group membership to app roles for JIT role assignment on
// login. Pure — no DB, no HTTP — so it is trivially unit-testable; the Identity
// side (reading claims, writing roles) lives in Services/EntraRoleSynchronizer.
//
// Reconcile model: on each Entra login we make the user's roles match their groups.
//   * Every role mapped from a group the user is in is GRANTED.
//   * Any *managed* role (one that appears as a mapping target) the user no longer
//     qualifies for is REVOKED.
//   * Roles that never appear as a mapping target are UNMANAGED and left untouched,
//     so hand-granted roles (or roles governed elsewhere) survive a sync. This is
//     what lets an org put, say, Manager/Finance under Entra while still assigning
//     Admin by hand.
public static class EntraRoleMapper
{
    // Validate raw config entries into usable maps. An entry is dropped when its
    // group id is blank or its role is not a known app role — a typo must never
    // silently grant (or, as a mapping target, revoke) access. Role name match is
    // case-insensitive but normalised back to the canonical Roles.* spelling.
    public static IReadOnlyList<EntraGroupRoleMap> ParseMappings(
        IEnumerable<EntraGroupRoleMap?>? raw)
    {
        if (raw is null) return Array.Empty<EntraGroupRoleMap>();

        var result = new List<EntraGroupRoleMap>();
        foreach (var m in raw)
        {
            if (m is null) continue;
            var group = m.GroupId?.Trim();
            var role = Canonical(m.Role);
            if (string.IsNullOrEmpty(group) || role is null) continue;
            result.Add(new EntraGroupRoleMap(group, role));
        }
        return result;
    }

    // Pass-through mode: the Entra *app-role* values ARE our role names, so no mapping
    // table is needed — a claim value of "Manager" grants Manager. The directory owns
    // the whole role model, so every app role is "managed" (mirrored on each login),
    // Employee included.
    //
    // Employee is a *fallback floor*, not a permanent addition: it is granted only when
    // the token carries no other known app role, so an authenticated user is never left
    // role-less. As soon as the directory grants an elevated role, the redundant
    // Employee is mirrored away — each user ends up with a single effective role, which
    // matches how local/seeded accounts are provisioned (a Manager is just Manager, not
    // Employee+Manager). Claim values that aren't known app roles are ignored. Returns
    // (desired, managed) ready to hand to Reconcile.
    public static (IReadOnlySet<string> Desired, IReadOnlySet<string> Managed) PassthroughPlan(
        IEnumerable<string> claimValues)
    {
        var desired = claimValues
            .Select(Canonical)
            .Where(r => r is not null)
            .Select(r => r!)
            .ToHashSet(StringComparer.Ordinal);

        // Floor only when the directory granted nothing else.
        if (desired.Count == 0)
            desired.Add(Roles.Employee);

        // Directory is authoritative for the entire model, Employee included, so a
        // now-redundant floor is revoked once an elevated role applies.
        var managed = Roles.All.ToHashSet(StringComparer.Ordinal);

        return (desired, managed);
    }

    // Claim values that are NOT known app roles, de-duplicated. Surfaced so a
    // misconfigured Entra app-role *Value* — which the assignment UI hides behind the
    // role's Display name — is diagnosable in logs instead of being silently dropped
    // (PassthroughPlan ignores them). Blank values are skipped.
    public static IReadOnlyList<string> UnknownRoleValues(IEnumerable<string> claimValues) =>
        claimValues
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Where(v => Canonical(v) is null)
            .Select(v => v.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    // Canonical Roles.* spelling for a value, matched case-insensitively; null when
    // the value is not a known app role (typo / unrelated claim value).
    private static string? Canonical(string? value) =>
        Roles.All.FirstOrDefault(r =>
            string.Equals(r, value?.Trim(), StringComparison.OrdinalIgnoreCase));

    // The distinct set of roles that group mappings govern. Only these are ever
    // revoked by a sync; every other role the user holds is left as-is.
    public static IReadOnlySet<string> ManagedRoles(IEnumerable<EntraGroupRoleMap> maps) =>
        maps.Select(m => m.Role).ToHashSet(StringComparer.Ordinal);

    // Roles the user should hold given the group ids present on their token.
    // Group-id comparison is case-insensitive (Entra object-id GUIDs are supplied
    // in mixed case across tools).
    public static IReadOnlySet<string> DesiredRoles(
        IEnumerable<string> groupIds, IEnumerable<EntraGroupRoleMap> maps)
    {
        var groups = groupIds
            .Where(g => !string.IsNullOrWhiteSpace(g))
            .Select(g => g.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return maps.Where(m => groups.Contains(m.GroupId))
            .Select(m => m.Role)
            .ToHashSet(StringComparer.Ordinal);
    }

    // Minimal change set. ToAdd = desired roles the user lacks. ToRemove = managed
    // roles the user holds but no longer qualifies for. Unmanaged roles never appear
    // in either list, so a sync can only touch roles that Entra actually governs.
    public static (IReadOnlyList<string> ToAdd, IReadOnlyList<string> ToRemove) Reconcile(
        IEnumerable<string> currentRoles,
        IReadOnlySet<string> desiredRoles,
        IReadOnlySet<string> managedRoles)
    {
        var current = currentRoles.ToHashSet(StringComparer.Ordinal);

        var toAdd = desiredRoles.Where(r => !current.Contains(r)).ToList();
        var toRemove = current
            .Where(r => managedRoles.Contains(r) && !desiredRoles.Contains(r))
            .ToList();

        return (toAdd, toRemove);
    }
}
