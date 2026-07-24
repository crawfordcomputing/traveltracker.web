using TravelTracker.Web.Domain;
using Xunit;

namespace TravelTracker.Tests;

// The absolute session cap (measured from first sign-in, survives sliding renewal).
public class SessionPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Not_expired_within_the_window()
    {
        var start = Now.AddHours(-7); // 7h into an 8h cap
        Assert.False(SessionPolicy.IsAbsolutelyExpired(start, Now, TimeSpan.FromHours(8)));
    }

    [Fact]
    public void Expired_past_the_window()
    {
        var start = Now.AddHours(-9);
        Assert.True(SessionPolicy.IsAbsolutelyExpired(start, Now, TimeSpan.FromHours(8)));
    }

    [Fact]
    public void Expired_exactly_at_the_boundary()
    {
        var start = Now.AddHours(-8);
        Assert.True(SessionPolicy.IsAbsolutelyExpired(start, Now, TimeSpan.FromHours(8)));
    }

    [Fact]
    public void Missing_start_never_expires()
    {
        // No stamp (e.g. a legacy cookie issued before this feature) is treated as live;
        // the idle timeout still bounds it.
        Assert.False(SessionPolicy.IsAbsolutelyExpired(null, Now, TimeSpan.FromHours(8)));
    }
}
