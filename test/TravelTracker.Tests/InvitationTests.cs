using TravelTracker.Web.Data.Entities;
using TravelTracker.Web.Domain;
using Xunit;

namespace TravelTracker.Tests;

public class InvitationTests
{
    [Fact]
    public void NewToken_is_urlsafe_and_unique()
    {
        var a = InviteTokens.NewToken();
        var b = InviteTokens.NewToken();
        Assert.NotEqual(a, b);
        Assert.DoesNotContain('+', a);
        Assert.DoesNotContain('/', a);
        Assert.DoesNotContain('=', a);
        Assert.True(a.Length >= 40);
    }

    [Fact]
    public void Hash_is_deterministic_and_hides_token()
    {
        var token = InviteTokens.NewToken();
        Assert.Equal(InviteTokens.Hash(token), InviteTokens.Hash(token));
        Assert.NotEqual(token, InviteTokens.Hash(token));
        Assert.Equal(64, InviteTokens.Hash(token).Length); // SHA-256 hex
    }

    [Fact]
    public void IsRedeemable_true_when_unaccepted_and_unexpired()
    {
        var now = DateTimeOffset.UtcNow;
        var invite = new Invitation { ExpiresAt = now.AddDays(1) };
        Assert.True(invite.IsRedeemable(now));
    }

    [Fact]
    public void IsRedeemable_false_when_expired()
    {
        var now = DateTimeOffset.UtcNow;
        var invite = new Invitation { ExpiresAt = now.AddSeconds(-1) };
        Assert.False(invite.IsRedeemable(now));
    }

    [Fact]
    public void IsRedeemable_false_when_already_accepted()
    {
        var now = DateTimeOffset.UtcNow;
        var invite = new Invitation { ExpiresAt = now.AddDays(1), AcceptedAt = now.AddMinutes(-5) };
        Assert.False(invite.IsRedeemable(now));
    }
}
