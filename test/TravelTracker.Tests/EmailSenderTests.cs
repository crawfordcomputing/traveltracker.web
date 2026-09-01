using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TravelTracker.Web.Data;
using TravelTracker.Web.Services;
using Xunit;

namespace TravelTracker.Tests;

// Covers the Email:Provider switch (Log default, Smtp opt-in) and that the dev
// LoggingEmailSender completes without needing any SMTP configuration.
//
// Every provider is wrapped in AuditingEmailSender (records outcomes to
// NotificationLog), so resolution returns the decorator; the tests assert the
// selected concrete sender via AuditingEmailSender.Inner.
public class EmailSenderTests
{
    private static IEmailSender ResolveInner(string? provider)
    {
        var dict = new Dictionary<string, string?>();
        if (provider is not null) dict["Email:Provider"] = provider;
        var config = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddLogging();
        // The decorator takes an AppDbContext; constructing one opens no connection,
        // so a non-connecting provider is enough to resolve the graph in a unit test.
        services.AddDbContext<AppDbContext>(o => o.UseSqlServer("Server=unused;Database=unused;"));
        services.AddAppEmail(config);

        var resolved = services.BuildServiceProvider().GetRequiredService<IEmailSender>();
        return Assert.IsType<AuditingEmailSender>(resolved).Inner;
    }

    [Fact]
    public void Defaults_to_logging_sender_when_unset() =>
        Assert.IsType<LoggingEmailSender>(ResolveInner(null));

    [Theory]
    [InlineData("Log")]
    [InlineData("nonsense")]   // unknown value falls back to the safe dev default
    public void Log_and_unknown_values_use_logging_sender(string provider) =>
        Assert.IsType<LoggingEmailSender>(ResolveInner(provider));

    [Theory]
    [InlineData("Smtp")]
    [InlineData("smtp")]       // case-insensitive
    public void Smtp_value_selects_smtp_sender(string provider) =>
        Assert.IsType<SmtpEmailSender>(ResolveInner(provider));

    [Fact]
    public async Task LoggingEmailSender_completes_without_smtp()
    {
        var sender = new LoggingEmailSender(NullLogger<LoggingEmailSender>.Instance);
        await sender.SendAsync("someone@example.com", "Subject", "<p>Body</p>");
    }
}
