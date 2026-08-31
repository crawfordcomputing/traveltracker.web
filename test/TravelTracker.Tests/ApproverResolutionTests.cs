using TravelTracker.Web.Domain;
using Xunit;

namespace TravelTracker.Tests;

// Pure effective-approver rules: personal ApproverId wins, then the department
// default, then nothing — and a user can never resolve to approving themselves.
public class ApproverResolutionTests
{
    [Fact]
    public void Personal_Approver_Wins_Over_Department_Default()
    {
        var id = ApproverResolution.EffectiveApproverId("me", personalApproverId: "boss", departmentDefaultApproverId: "dept");
        Assert.Equal("boss", id);
    }

    [Fact]
    public void Falls_Back_To_Department_Default_When_No_Personal()
    {
        var id = ApproverResolution.EffectiveApproverId("me", personalApproverId: null, departmentDefaultApproverId: "dept");
        Assert.Equal("dept", id);
    }

    [Fact]
    public void Empty_Personal_Is_Treated_As_Unset()
    {
        var id = ApproverResolution.EffectiveApproverId("me", personalApproverId: "", departmentDefaultApproverId: "dept");
        Assert.Equal("dept", id);
    }

    [Fact]
    public void Null_When_Neither_Is_Set()
    {
        Assert.Null(ApproverResolution.EffectiveApproverId("me", null, null));
    }

    [Fact]
    public void Null_When_Only_Candidate_Is_The_Traveler_Personal()
    {
        // A user set as their own personal approver never resolves to themselves.
        Assert.Null(ApproverResolution.EffectiveApproverId("me", personalApproverId: "me", departmentDefaultApproverId: null));
    }

    [Fact]
    public void Null_When_Department_Default_Is_The_Traveler()
    {
        // Member is their own department's default and has no personal approver.
        Assert.Null(ApproverResolution.EffectiveApproverId("me", personalApproverId: null, departmentDefaultApproverId: "me"));
    }

    [Fact]
    public void Personal_Approver_Still_Wins_Even_If_Department_Default_Is_Self()
    {
        var id = ApproverResolution.EffectiveApproverId("me", personalApproverId: "boss", departmentDefaultApproverId: "me");
        Assert.Equal("boss", id);
    }
}
