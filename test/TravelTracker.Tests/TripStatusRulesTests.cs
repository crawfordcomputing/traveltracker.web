using TravelTracker.Web.Data.Entities;
using TravelTracker.Web.Domain;
using Xunit;

namespace TravelTracker.Tests;

public class TripStatusRulesTests
{
    [Theory]
    // Draft: traveler submits or cancels.
    [InlineData(TripStatus.Draft, TripStatus.Submitted, true)]
    [InlineData(TripStatus.Draft, TripStatus.Cancelled, true)]
    [InlineData(TripStatus.Draft, TripStatus.Approved, false)]
    [InlineData(TripStatus.Draft, TripStatus.Completed, false)]
    // Submitted: approver decides, or traveler withdraws.
    [InlineData(TripStatus.Submitted, TripStatus.Approved, true)]
    [InlineData(TripStatus.Submitted, TripStatus.Rejected, true)]
    [InlineData(TripStatus.Submitted, TripStatus.Draft, true)]
    [InlineData(TripStatus.Submitted, TripStatus.Completed, false)]
    // Approved: traveler completes or cancels.
    [InlineData(TripStatus.Approved, TripStatus.Completed, true)]
    [InlineData(TripStatus.Approved, TripStatus.Cancelled, true)]
    // Rejected: traveler revises (back to draft) or cancels.
    [InlineData(TripStatus.Rejected, TripStatus.Draft, true)]
    [InlineData(TripStatus.Rejected, TripStatus.Cancelled, true)]
    // Terminal-ish reopen paths.
    [InlineData(TripStatus.Completed, TripStatus.Approved, true)]
    [InlineData(TripStatus.Cancelled, TripStatus.Draft, true)]
    // Legacy Planned behaves like Approved.
    [InlineData(TripStatus.Planned, TripStatus.Completed, true)]
    [InlineData(TripStatus.Planned, TripStatus.Cancelled, true)]
    public void CanTransition_Matches_Table(TripStatus from, TripStatus to, bool expected)
        => Assert.Equal(expected, TripStatusRules.CanTransition(from, to));

    [Theory]
    // Approve/reject are approver-only; the traveler may not perform them.
    [InlineData(TripStatus.Submitted, TripStatus.Approved, TripActor.Approver, true)]
    [InlineData(TripStatus.Submitted, TripStatus.Approved, TripActor.Traveler, false)]
    [InlineData(TripStatus.Submitted, TripStatus.Rejected, TripActor.Approver, true)]
    [InlineData(TripStatus.Submitted, TripStatus.Rejected, TripActor.Traveler, false)]
    // Submit/withdraw/complete are traveler-only; the approver may not.
    [InlineData(TripStatus.Draft, TripStatus.Submitted, TripActor.Traveler, true)]
    [InlineData(TripStatus.Draft, TripStatus.Submitted, TripActor.Approver, false)]
    [InlineData(TripStatus.Approved, TripStatus.Completed, TripActor.Traveler, true)]
    [InlineData(TripStatus.Approved, TripStatus.Completed, TripActor.Approver, false)]
    public void CanTransition_Respects_Actor(TripStatus from, TripStatus to, TripActor actor, bool expected)
        => Assert.Equal(expected, TripStatusRules.CanTransition(from, to, actor));

    [Fact]
    public void NextStates_From_Draft_Are_Submit_Or_Cancel()
    {
        var next = TripStatusRules.NextStates(TripStatus.Draft);
        Assert.Contains(TripStatus.Submitted, next);
        Assert.Contains(TripStatus.Cancelled, next);
        Assert.DoesNotContain(TripStatus.Approved, next);
    }

    [Fact]
    public void TravelerTransitions_From_Submitted_Exclude_Decisions()
    {
        var traveler = TripStatusRules.TransitionsFor(TripStatus.Submitted, TripActor.Traveler);
        Assert.Contains(traveler, t => t.To == TripStatus.Draft);       // withdraw
        Assert.DoesNotContain(traveler, t => t.To == TripStatus.Approved);
        Assert.DoesNotContain(traveler, t => t.To == TripStatus.Rejected);

        var approver = TripStatusRules.TransitionsFor(TripStatus.Submitted, TripActor.Approver);
        Assert.Contains(approver, t => t.To == TripStatus.Approved);
        Assert.Contains(approver, t => t.To == TripStatus.Rejected);
    }
}
