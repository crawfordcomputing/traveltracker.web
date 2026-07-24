using TravelTracker.Web.Domain;
using Xunit;

namespace TravelTracker.Tests;

// Pure cycle-detection rules for the AppUser.ApproverId self-reference.
public class ApproverGraphTests
{
    private static IReadOnlyDictionary<string, string?> Chain(params (string User, string? Approver)[] edges)
        => edges.ToDictionary(e => e.User, e => e.Approver);

    [Fact]
    public void Null_Approver_Is_Never_A_Cycle()
    {
        Assert.False(ApproverGraph.CreatesCycle("u1", null, Chain()));
    }

    [Fact]
    public void Self_Approval_Is_A_Cycle()
    {
        Assert.True(ApproverGraph.CreatesCycle("u1", "u1", Chain()));
    }

    [Fact]
    public void Direct_Reciprocal_Is_A_Cycle()
    {
        // a's approver is b. Setting b's approver to a closes a 2-cycle.
        var chain = Chain(("a", "b"));
        Assert.True(ApproverGraph.CreatesCycle("b", "a", chain));
    }

    [Fact]
    public void Transitive_Cycle_Is_Detected()
    {
        // a -> b -> c already. Setting c's approver to a closes a 3-cycle.
        var chain = Chain(("a", "b"), ("b", "c"));
        Assert.True(ApproverGraph.CreatesCycle("c", "a", chain));
    }

    [Fact]
    public void Acyclic_Assignment_Is_Allowed()
    {
        // b -> c exists; giving a the approver b is a plain chain, no cycle.
        var chain = Chain(("b", "c"));
        Assert.False(ApproverGraph.CreatesCycle("a", "b", chain));
    }

    [Fact]
    public void Shared_Approver_Is_Not_A_Cycle()
    {
        // Both a and the target already point at manager m; assigning m again to a
        // different report is fine.
        var chain = Chain(("a", "m"));
        Assert.False(ApproverGraph.CreatesCycle("x", "m", chain));
    }

    [Fact]
    public void Preexisting_Loop_In_Data_Does_Not_Hang()
    {
        // Corrupt data: p <-> q already loop. Resolver must terminate, not spin.
        var chain = Chain(("p", "q"), ("q", "p"));
        Assert.True(ApproverGraph.CreatesCycle("z", "p", chain));
    }
}
