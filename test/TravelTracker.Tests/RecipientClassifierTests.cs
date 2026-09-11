using Microsoft.Extensions.Configuration;
using TravelTracker.Web.Data.Entities;
using TravelTracker.Web.Domain;
using Xunit;

namespace TravelTracker.Tests;

// ADR-0005 internal/external classification by exact recipient domain.
public class RecipientClassifierTests
{
    private static IConfiguration Config(params (string Key, string Value)[] pairs) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(pairs.ToDictionary(p => p.Key, p => (string?)p.Value))
            .Build();

    private static readonly IConfiguration Ours = Config(
        ("Email:InternalDomains:0", "example.com"),
        ("Email:InternalDomains:1", "corp.example.com"));

    [Theory]
    [InlineData("pat@example.com", EmailAudience.Internal)]
    [InlineData("PAT@Example.COM", EmailAudience.Internal)]      // case-insensitive
    [InlineData("pat@corp.example.com", EmailAudience.Internal)] // listed subdomain
    [InlineData("pat@other.example.com", EmailAudience.External)] // unlisted subdomain
    [InlineData("pat@example.com.evil.io", EmailAudience.External)] // lookalike
    [InlineData("pat@contractor.io", EmailAudience.External)]
    [InlineData("not-an-email", EmailAudience.External)]
    [InlineData("", EmailAudience.External)]
    public void Classifies_By_Exact_Domain(string email, EmailAudience expected) =>
        Assert.Equal(expected, RecipientClassifier.Classify(Ours, email));

    [Fact]
    public void Falls_Back_To_Registration_Allowed_Domains()
    {
        var config = Config(("Auth:Registration:AllowedDomains:0", "example.com"));

        Assert.Equal(EmailAudience.Internal, RecipientClassifier.Classify(config, "a@example.com"));
        Assert.Equal(EmailAudience.External, RecipientClassifier.Classify(config, "a@else.com"));
    }

    [Fact]
    public void Internal_Domains_Win_Over_Registration_Domains()
    {
        var config = Config(
            ("Email:InternalDomains:0", "staff.example.com"),
            ("Auth:Registration:AllowedDomains:0", "example.com"));

        Assert.Equal(EmailAudience.External, RecipientClassifier.Classify(config, "a@example.com"));
        Assert.Equal(EmailAudience.Internal, RecipientClassifier.Classify(config, "a@staff.example.com"));
    }

    [Fact]
    public void No_Configured_Domains_Means_Any()
    {
        Assert.Equal(EmailAudience.Any, RecipientClassifier.Classify(Config(), "a@example.com"));
        // Blank entries don't count as configuration.
        Assert.Equal(EmailAudience.Any,
            RecipientClassifier.Classify(Config(("Email:InternalDomains:0", "  ")), "a@example.com"));
    }
}
