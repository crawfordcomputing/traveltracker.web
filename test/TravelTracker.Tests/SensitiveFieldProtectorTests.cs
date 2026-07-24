using Microsoft.AspNetCore.DataProtection;
using TravelTracker.Web.Services;
using Xunit;

namespace TravelTracker.Tests;

public class SensitiveFieldProtectorTests
{
    private static SensitiveFieldProtector New()
        => new(DataProtectionProvider.Create("TravelTracker.Tests"));

    [Fact]
    public void Protect_Then_Unprotect_Round_Trips()
    {
        var p = New();
        var cipher = p.Protect("X1234567");
        Assert.NotNull(cipher);
        Assert.NotEqual("X1234567", cipher);          // not stored in the clear
        Assert.Equal("X1234567", p.Unprotect(cipher));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Empty_Input_Maps_To_Null(string? input)
    {
        var p = New();
        Assert.Null(p.Protect(input));
        Assert.Null(p.Unprotect(input));
    }

    [Fact]
    public void Undecryptable_Ciphertext_Returns_Null_Not_Throw()
    {
        var p = New();
        Assert.Null(p.Unprotect("not-valid-ciphertext"));
    }

    [Fact]
    public void Ciphertext_From_A_Different_Key_Ring_Returns_Null()
    {
        var a = New();
        var other = new SensitiveFieldProtector(DataProtectionProvider.Create("Some.Other.App"));
        var cipher = a.Protect("secret");
        Assert.Null(other.Unprotect(cipher)); // wrong key ring -> fail-safe null
    }
}
