using TravelTracker.Web.Data.Entities;
using TravelTracker.Web.Domain;
using Xunit;

namespace TravelTracker.Tests;

// Pure-domain coverage for the Entra group->role JIT mapping. No DB: all the
// decision logic lives here; the Identity I/O side (EntraRoleSynchronizer) is thin
// glue over UserManager + AdminGuard, both already covered elsewhere.
public class EntraRoleMapperTests
{
    private const string GmMgr = "11111111-1111-1111-1111-111111111111";
    private const string GmFin = "22222222-2222-2222-2222-222222222222";
    private const string GmNone = "99999999-9999-9999-9999-999999999999";

    private static EntraGroupRoleMap Map(string g, string r) => new(g, r);

    // --- ParseMappings ------------------------------------------------------

    [Fact]
    public void ParseMappings_drops_unknown_roles_and_blank_groups()
    {
        var parsed = EntraRoleMapper.ParseMappings(new EntraGroupRoleMap?[]
        {
            Map(GmMgr, "Manager"),
            Map(GmFin, "Wizard"),   // not a real role -> dropped
            Map("   ", "Admin"),    // blank group -> dropped
            null,                    // null entry -> dropped
        });

        var only = Assert.Single(parsed);
        Assert.Equal(GmMgr, only.GroupId);
        Assert.Equal(Roles.Manager, only.Role);
    }

    [Fact]
    public void ParseMappings_normalises_role_casing_to_canonical()
    {
        var parsed = EntraRoleMapper.ParseMappings(new EntraGroupRoleMap?[] { Map(GmFin, "fInAnCe") });
        Assert.Equal(Roles.Finance, Assert.Single(parsed).Role);
    }

    [Fact]
    public void ParseMappings_null_input_is_empty()
        => Assert.Empty(EntraRoleMapper.ParseMappings(null));

    // --- DesiredRoles -------------------------------------------------------

    [Fact]
    public void DesiredRoles_grants_roles_for_groups_the_user_is_in()
    {
        var maps = new[] { Map(GmMgr, Roles.Manager), Map(GmFin, Roles.Finance) };

        var desired = EntraRoleMapper.DesiredRoles(new[] { GmMgr }, maps);

        Assert.Equal(new[] { Roles.Manager }, desired);
    }

    [Fact]
    public void DesiredRoles_is_empty_when_no_group_matches()
    {
        var maps = new[] { Map(GmMgr, Roles.Manager) };
        Assert.Empty(EntraRoleMapper.DesiredRoles(new[] { GmNone }, maps));
    }

    [Fact]
    public void DesiredRoles_matches_group_ids_case_insensitively()
    {
        var maps = new[] { Map(GmMgr, Roles.Manager) };
        var desired = EntraRoleMapper.DesiredRoles(new[] { GmMgr.ToUpperInvariant() }, maps);
        Assert.Contains(Roles.Manager, desired);
    }

    [Fact]
    public void DesiredRoles_unions_multiple_groups()
    {
        var maps = new[] { Map(GmMgr, Roles.Manager), Map(GmFin, Roles.Finance) };
        var desired = EntraRoleMapper.DesiredRoles(new[] { GmMgr, GmFin }, maps);
        Assert.Equal(new[] { Roles.Finance, Roles.Manager }, desired.OrderBy(x => x));
    }

    // --- Reconcile ----------------------------------------------------------

    [Fact]
    public void Reconcile_adds_desired_role_the_user_lacks()
    {
        var maps = new[] { Map(GmMgr, Roles.Manager) };
        var (toAdd, toRemove) = EntraRoleMapper.Reconcile(
            currentRoles: new[] { Roles.Employee },
            desiredRoles: EntraRoleMapper.DesiredRoles(new[] { GmMgr }, maps),
            managedRoles: EntraRoleMapper.ManagedRoles(maps));

        Assert.Equal(new[] { Roles.Manager }, toAdd);
        Assert.Empty(toRemove);
    }

    [Fact]
    public void Reconcile_revokes_managed_role_when_group_dropped()
    {
        var maps = new[] { Map(GmMgr, Roles.Manager) };
        // User currently holds Manager but is no longer in the mapped group.
        var (toAdd, toRemove) = EntraRoleMapper.Reconcile(
            currentRoles: new[] { Roles.Employee, Roles.Manager },
            desiredRoles: EntraRoleMapper.DesiredRoles(Array.Empty<string>(), maps),
            managedRoles: EntraRoleMapper.ManagedRoles(maps));

        Assert.Empty(toAdd);
        Assert.Equal(new[] { Roles.Manager }, toRemove);
    }

    [Fact]
    public void Reconcile_leaves_unmanaged_roles_untouched()
    {
        // Only Manager is governed by a mapping. The user's hand-granted Admin and
        // baseline Employee must survive even though no group grants them.
        var maps = new[] { Map(GmMgr, Roles.Manager) };
        var (toAdd, toRemove) = EntraRoleMapper.Reconcile(
            currentRoles: new[] { Roles.Employee, Roles.Admin },
            desiredRoles: EntraRoleMapper.DesiredRoles(new[] { GmMgr }, maps),
            managedRoles: EntraRoleMapper.ManagedRoles(maps));

        Assert.Equal(new[] { Roles.Manager }, toAdd);
        Assert.Empty(toRemove); // Admin is unmanaged -> not revoked
    }

    [Fact]
    public void Reconcile_is_noop_when_roles_already_match()
    {
        var maps = new[] { Map(GmMgr, Roles.Manager) };
        var (toAdd, toRemove) = EntraRoleMapper.Reconcile(
            currentRoles: new[] { Roles.Employee, Roles.Manager },
            desiredRoles: EntraRoleMapper.DesiredRoles(new[] { GmMgr }, maps),
            managedRoles: EntraRoleMapper.ManagedRoles(maps));

        Assert.Empty(toAdd);
        Assert.Empty(toRemove);
    }

    [Fact]
    public void Reconcile_empty_mapping_touches_nothing()
    {
        var maps = EntraRoleMapper.ParseMappings(Array.Empty<EntraGroupRoleMap?>());
        var (toAdd, toRemove) = EntraRoleMapper.Reconcile(
            currentRoles: new[] { Roles.Employee, Roles.Manager },
            desiredRoles: EntraRoleMapper.DesiredRoles(new[] { GmMgr }, maps),
            managedRoles: EntraRoleMapper.ManagedRoles(maps));

        Assert.Empty(toAdd);
        Assert.Empty(toRemove);
    }

    // --- PassthroughPlan (app-role pass-through) ----------------------------

    [Fact]
    public void Passthrough_honours_matching_role_values_and_ignores_unknown()
    {
        var (desired, _) = EntraRoleMapper.PassthroughPlan(new[] { "Manager", "Finance", "SomethingElse" });

        Assert.Contains(Roles.Manager, desired);
        Assert.Contains(Roles.Finance, desired);
        Assert.DoesNotContain("SomethingElse", desired);
    }

    [Fact]
    public void Passthrough_matches_case_insensitively_and_normalises()
    {
        var (desired, _) = EntraRoleMapper.PassthroughPlan(new[] { "aDmIn" });
        Assert.Contains(Roles.Admin, desired);
    }

    [Fact]
    public void Passthrough_applies_employee_floor_only_when_no_other_role()
    {
        var (desired, managed) = EntraRoleMapper.PassthroughPlan(Array.Empty<string>());

        Assert.Contains(Roles.Employee, desired);   // floor when no claims
        Assert.Contains(Roles.Employee, managed);   // now managed, so it can be replaced
    }

    [Fact]
    public void Passthrough_omits_employee_floor_when_an_elevated_role_is_present()
    {
        var (desired, _) = EntraRoleMapper.PassthroughPlan(new[] { "Admin" });

        Assert.Contains(Roles.Admin, desired);
        Assert.DoesNotContain(Roles.Employee, desired); // elevated role stands alone
    }

    [Fact]
    public void Passthrough_manages_every_role_including_employee()
    {
        var (_, managed) = EntraRoleMapper.PassthroughPlan(Array.Empty<string>());

        Assert.Equal(Roles.All.OrderBy(x => x), managed.OrderBy(x => x));
    }

    [Fact]
    public void Passthrough_reconcile_revokes_a_role_no_longer_assigned()
    {
        // User currently Employee+Manager; token now carries only "Finance".
        var (desired, managed) = EntraRoleMapper.PassthroughPlan(new[] { "Finance" });
        var (toAdd, toRemove) = EntraRoleMapper.Reconcile(
            currentRoles: new[] { Roles.Employee, Roles.Manager },
            desiredRoles: desired,
            managedRoles: managed);

        Assert.Equal(new[] { Roles.Finance }, toAdd);
        // Both the old elevated role and the now-redundant floor are mirrored away,
        // leaving Finance as the single effective role.
        Assert.Equal(new[] { Roles.Employee, Roles.Manager }.OrderBy(x => x), toRemove.OrderBy(x => x));
    }

    [Fact]
    public void Passthrough_reconcile_replaces_floor_with_elevated_role()
    {
        // Freshly provisioned user starts on the Employee floor; token carries "Admin".
        var (desired, managed) = EntraRoleMapper.PassthroughPlan(new[] { "Admin" });
        var (toAdd, toRemove) = EntraRoleMapper.Reconcile(
            currentRoles: new[] { Roles.Employee },
            desiredRoles: desired,
            managedRoles: managed);

        Assert.Equal(new[] { Roles.Admin }, toAdd);
        Assert.Equal(new[] { Roles.Employee }, toRemove); // floor replaced, single role
    }
}
