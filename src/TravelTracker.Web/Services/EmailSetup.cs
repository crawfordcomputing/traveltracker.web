namespace TravelTracker.Web.Services;

// Provider switch for outbound email: log-to-console by default (frictionless dev),
// real SMTP when Email:Provider=Smtp. Same "sensible default, one config switch to
// production" pattern used for Storage:Provider and Auth:EnableEntraId.
public static class EmailSetup
{
    public static IServiceCollection AddAppEmail(
        this IServiceCollection services, IConfiguration config)
    {
        var provider = config["Email:Provider"] ?? "Log";

        switch (provider.ToLowerInvariant())
        {
            case "smtp":
                services.AddScoped<IEmailSender, SmtpEmailSender>();
                break;

            case "log":
            default:
                services.AddScoped<IEmailSender, LoggingEmailSender>();
                break;
        }

        // Reused by self-serve registration and the resend-confirmation page.
        services.AddScoped<EmailConfirmationService>();

        return services;
    }
}
