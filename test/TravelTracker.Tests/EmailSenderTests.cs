using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TravelTracker.Web.Services;
using Xunit;

namespace TravelTracker.Tests;

// Covers the Email:Provider switch (Log default, Smtp opt-in) and that the dev
// LoggingEmailSender completes without needing any SMTP configuration.
public class EmailSenderTests
{
    private static IEmailSender Resolve(string? provider)
    {
        var dict = new Dictionary<string, string?>();
        if (provider is not null) dict["Email:Provider"] = provider;
        var config = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddLogging();
        services.AddAppEmail(config);
        return services.BuildServiceProvider().GetRequiredService<IEmailSender>();
    }

    [Fact]
    public void Defaults_to_logging_sender_when_unset() =>
        Assert.IsType<LoggingEmailSender>(Resolve(null));

    [Theory]
    [InlineData("Log")]
    [InlineData("nonsense")]   // unknown value falls back to the safe dev default
    public void Log_and_unknown_values_use_logging_sender(string provider) =>
        Assert.IsType<LoggingEmailSender>(Resolve(provider));

    [Theory]
    [InlineData("Smtp")]
    [InlineData("smtp")]       // case-insensitive
    public void Smtp_value_selects_smtp_sender(string provider) =>
        Assert.IsType<SmtpEmailSender>(Resolve(provider));

    [Fact]
    public async Task LoggingEmailSender_completes_without_smtp()
    {
        var sender = new LoggingEmailSender(NullLogger<LoggingEmailSender>.Instance);
        await sender.SendAsync("someone@example.com", "Subject", "<p>Body</p>");
    }
}
