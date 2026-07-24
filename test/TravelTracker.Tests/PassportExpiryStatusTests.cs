using TravelTracker.Web.Domain;
using Xunit;

namespace TravelTracker.Tests;

public class PassportExpiryStatusTests
{
    private static readonly DateOnly Today = new(2026, 7, 19);

    [Fact]
    public void Null_Expiry_Is_Unknown()
        => Assert.Equal(PassportValidity.Unknown, PassportExpiryStatus.Evaluate(null, Today));

    [Fact]
    public void Past_Expiry_Is_Expired()
        => Assert.Equal(PassportValidity.Expired,
            PassportExpiryStatus.Evaluate(Today.AddDays(-1), Today));

    [Fact]
    public void Within_Threshold_Is_Expiring_Soon()
        => Assert.Equal(PassportValidity.ExpiringSoon,
            PassportExpiryStatus.Evaluate(Today.AddMonths(3), Today));

    [Fact]
    public void On_Threshold_Boundary_Is_Expiring_Soon()
        => Assert.Equal(PassportValidity.ExpiringSoon,
            PassportExpiryStatus.Evaluate(Today.AddMonths(6), Today));

    [Fact]
    public void Beyond_Threshold_Is_Ok()
        => Assert.Equal(PassportValidity.Ok,
            PassportExpiryStatus.Evaluate(Today.AddMonths(6).AddDays(1), Today));

    [Fact]
    public void Expiring_Today_Is_Expiring_Soon_Not_Expired()
        => Assert.Equal(PassportValidity.ExpiringSoon,
            PassportExpiryStatus.Evaluate(Today, Today));
}
