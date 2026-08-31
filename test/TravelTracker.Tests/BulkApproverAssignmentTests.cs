using TravelTracker.Web.Domain;
using Xunit;

namespace TravelTracker.Tests;

// Pure planning rules for the Users-list bulk "set one approver on many users"
// action: which users take the approver and which are skipped (and why).
public class BulkApproverAssignmentTests
{
    private static Dictionary<string, string?> Chain(params (string User, string? Approver)[] edges)
        => edges.ToDictionary(e => e.User, e => e.Approver);

    [Fact]
    public void Assigns_All_Clean_Users()
    {
        var (assign, skipped) = ApproverGraph.PlanBulkApproverAssignment(
            "boss", new[] { "a", "b", "c" }, Chain());

        Assert.Equal(new[] { "a", "b", "c" }, assign);
        Assert.Empty(skipped);
    }

    [Fact]
    public void Skips_The_Approver_Assigned_To_Itself()
    {
        var (assign, skipped) = ApproverGraph.PlanBulkApproverAssignment(
            "boss", new[] { "a", "boss", "b" }, Chain());

        Assert.Equal(new[] { "a", "b" }, assign);
        var s = Assert.Single(skipped);
        Assert.Equal("boss", s.UserId);
        Assert.Equal(ApproverGraph.BulkSkipReason.SelfApproval, s.Reason);
    }

    [Fact]
    public void Skips_A_User_That_Would_Create_A_Cycle()
    {
        // boss already reports to 'a'. Assigning 'a' -> boss would close a 2-cycle.
        var chain = Chain(("boss", "a"));
        var (assign, skipped) = ApproverGraph.PlanBulkApproverAssignment(
            "boss", new[] { "a", "b" }, chain);

        Assert.Equal(new[] { "b" }, assign);
        var s = Assert.Single(skipped);
        Assert.Equal("a", s.UserId);
        Assert.Equal(ApproverGraph.BulkSkipReason.WouldCreateCycle, s.Reason);
    }

    [Fact]
    public void Catches_A_Cycle_Formed_Within_The_Same_Batch()
    {
        // Empty starting chain. In one batch we try to point both 'boss' and 'a' at
        // each other via the same target 'boss': 'boss'->itself is self-skip; but a
        // transitive in-batch case: target 'x', users [x's approver chain]. Model a
        // real in-batch chain: assign approver 'b' to users ['a','b_owner']... use a
        // concrete transitive case below.
        // Start: a -> (none), boss -> (none). Assign approver = 'a' to ['boss'] first
        // is clean; then in the SAME batch assigning 'a' with user 'a' is self. Build
        // a genuine in-batch cycle: approver 'top', users ['top_parent'] where
        // 'top' already -> 'mid', and we also include 'mid' pointing up.
        var chain = Chain(("top", "mid")); // top -> mid
        // Assign approver 'top' to users ['mid']: mid -> top closes mid<->top? mid->top
        // and top->mid = cycle. Should be skipped.
        var (assign, skipped) = ApproverGraph.PlanBulkApproverAssignment(
            "top", new[] { "mid" }, chain);

        Assert.Empty(assign);
        var s = Assert.Single(skipped);
        Assert.Equal("mid", s.UserId);
        Assert.Equal(ApproverGraph.BulkSkipReason.WouldCreateCycle, s.Reason);
    }

    [Fact]
    public void Mutates_Chain_So_Later_Rows_See_Earlier_Assignments()
    {
        // approver = 'c'. Batch: assign 'b' -> 'c' (clean), then 'a' -> 'c' (clean).
        // Also include 'c' itself (self-skip). Verify the chain reflects new edges.
        var chain = Chain();
        var (assign, skipped) = ApproverGraph.PlanBulkApproverAssignment(
            "c", new[] { "b", "a", "c" }, chain);

        Assert.Equal(new[] { "b", "a" }, assign);
        Assert.Equal("c", chain["b"]);
        Assert.Equal("c", chain["a"]);
        Assert.Single(skipped);
    }
}
