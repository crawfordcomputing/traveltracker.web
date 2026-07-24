using Microsoft.Extensions.Configuration;
using TravelTracker.Web.Domain;
using Xunit;

namespace TravelTracker.Tests;

public class RegistrationPolicyTests
{
    private static IConfiguration Config(string? mode, params string[] domains)
    {
        var dict = new Dictionary<string, string?>();
        if (mode is not null) dict["Auth:Registration:Mode"] = mode;
        for (var i = 0; i < domains.Length; i++)
            dict[$"Auth:Registration:AllowedDomains:{i}"] = domains[i];
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    [Theory]
    [InlineData("Open", RegistrationMode.Open)]
    [InlineData("domain", RegistrationMode.Domain)]   // case-insensitive
    [InlineData("Invite", RegistrationMode.Invite)]
    [InlineData("Closed", RegistrationMode.Closed)]
    public void Mode_parses_known_values(string raw, RegistrationMode expected) =>
        Assert.Equal(expected, RegistrationPolicy.Mode(Config(raw)));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nonsense")]
    public void Mode_fails_closed_on_bad_value(string? raw) =>
        Assert.Equal(RegistrationMode.Closed, RegistrationPolicy.Mode(Config(raw)));

    [Theory]
    [InlineData("Open", true)]
    [InlineData("Domain", true)]
    [InlineData("Invite", false)]
    [InlineData("Closed", false)]
    public void SelfServeAllowed_only_for_open_and_domain(string mode, bool expected) =>
        Assert.Equal(expected, RegistrationPolicy.SelfServeAllowed(Config(mode)));

    [Fact]
    public void Open_allows_any_email() =>
        Assert.True(RegistrationPolicy.EmailAllowed(Config("Open"), "anyone@wherever.io"));

    [Fact]
    public void Domain_allows_only_listed_hosts()
    {
        var c = Config("Domain", "yourco.com", "sub.yourco.com");
        Assert.True(RegistrationPolicy.EmailAllowed(c, "sam@yourco.com"));
        Assert.True(RegistrationPolicy.EmailAllowed(c, "SAM@YOURCO.COM"));   // case-insensitive
        Assert.True(RegistrationPolicy.EmailAllowed(c, "kit@sub.yourco.com"));
        Assert.False(RegistrationPolicy.EmailAllowed(c, "sam@evil.com"));
        Assert.False(RegistrationPolicy.EmailAllowed(c, "sam@notyourco.com"));
    }

    [Fact]
    public void Domain_with_empty_allowlist_denies_everyone() =>
        Assert.False(RegistrationPolicy.EmailAllowed(Config("Domain"), "sam@yourco.com"));

    [Theory]
    [InlineData("notanemail")]
    [InlineData("trailing@")]
    [InlineData("")]
    [InlineData(null)]
    public void Domain_rejects_malformed_email(string? email) =>
        Assert.False(RegistrationPolicy.EmailAllowed(Config("Domain", "yourco.com"), email));

    [Theory]
    [InlineData("Invite")]
    [InlineData("Closed")]
    public void Non_selfserve_modes_never_allow_email(string mode) =>
        Assert.False(RegistrationPolicy.EmailAllowed(Config(mode), "sam@yourco.com"));
}
