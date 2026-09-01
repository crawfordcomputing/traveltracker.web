namespace TravelTracker.Web.Services;

// Provider switch for outbound email: log-to-console by default (frictionless dev),
// real SMTP when Email:Provider=Smtp. Same "sensible default, one config switch to
// production" pattern used for Auth:EnableEntraId.
//
// Whichever concrete sender is chosen, it is wrapped in AuditingEmailSender so every
// send attempt is recorded to NotificationLog (surfaced at /Admin/Notifications).
public static class EmailSetup
{
    public static IServiceCollection AddAppEmail(
        this IServiceCollection services, IConfiguration config)
    {
        var provider = config["Email:Provider"] ?? "Log";

        switch (provider.ToLowerInvariant())
        {
            case "smtp":
                services.AddScoped<SmtpEmailSender>();
                services.AddScoped<IEmailSender>(sp => new AuditingEmailSender(
                    sp.GetRequiredService<SmtpEmailSender>(),
                    sp.GetRequiredService<Data.AppDbContext>(),
                    sp.GetRequiredService<ILogger<AuditingEmailSender>>()));
                break;

            case "log":
            default:
                services.AddScoped<LoggingEmailSender>();
                services.AddScoped<IEmailSender>(sp => new AuditingEmailSender(
                    sp.GetRequiredService<LoggingEmailSender>(),
                    sp.GetRequiredService<Data.AppDbContext>(),
                    sp.GetRequiredService<ILogger<AuditingEmailSender>>()));
                break;
        }

        // Reused by self-serve registration and the resend-confirmation page.
        services.AddScoped<EmailConfirmationService>();

        return services;
    }
}
