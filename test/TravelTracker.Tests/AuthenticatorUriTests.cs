using TravelTracker.Web.Domain;
using Xunit;

namespace TravelTracker.Tests;

// The otpauth:// provisioning URI and the manual-entry key formatting.
public class AuthenticatorUriTests
{
    [Fact]
    public void Build_produces_a_standard_totp_uri()
    {
        var uri = AuthenticatorUri.Build("user@example.com", "ABC123XYZ");

        Assert.StartsWith("otpauth://totp/", uri);
        Assert.Contains("secret=ABC123XYZ", uri);
        // Issuer/label are URL-encoded (space -> %20); UrlEncoder.Default leaves '@' as-is,
        // matching the standard Identity scaffolding — authenticator apps accept it.
        Assert.Contains("Travel%20Tracker:user@example.com", uri);
        Assert.Contains("issuer=Travel%20Tracker", uri);
        Assert.Contains("digits=6", uri);
    }

    [Fact]
    public void FormatKey_groups_into_lowercase_four_char_blocks()
    {
        Assert.Equal("abcd efgh ijkl", AuthenticatorUri.FormatKey("ABCDEFGHIJKL"));
    }

    [Fact]
    public void FormatKey_handles_a_trailing_partial_block()
    {
        Assert.Equal("abcd ef", AuthenticatorUri.FormatKey("ABCDEF"));
    }
}
