using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Mail;

namespace TravelTracker.Web.Services;

// Production sender: plain SMTP via System.Net.Mail (in the BCL, no extra package).
// Enabled with Email:Provider=Smtp. All settings come from the Email:* config
// section so switching to a real mail host is one config change, mirroring the
// Database/Storage provider pattern.
//
// Excluded from coverage: exercising this means standing up a live SMTP host, so
// it is verified manually / in integration rather than unit tests. Behaviour is
// thin config-to-SmtpClient plumbing with no branching logic worth asserting.
[ExcludeFromCodeCoverage]
public class SmtpEmailSender : IEmailSender
{
    private readonly IConfiguration _config;

    public SmtpEmailSender(IConfiguration config) => _config = config;

    public async Task SendAsync(string recipient, string subject, string htmlBody)
    {
        var host = _config["Email:Smtp:Host"]
            ?? throw new InvalidOperationException(
                "Email:Smtp:Host is required when Email:Provider=Smtp.");
        var port = _config.GetValue<int?>("Email:Smtp:Port") ?? 587;
        var user = _config["Email:Smtp:Username"];
        var password = _config["Email:Smtp:Password"];
        var from = _config["Email:FromAddress"] ?? user
            ?? throw new InvalidOperationException(
                "Email:FromAddress (or Email:Smtp:Username) is required when Email:Provider=Smtp.");

        using var client = new SmtpClient(host, port)
        {
            EnableSsl = _config.GetValue<bool?>("Email:Smtp:UseSsl") ?? true,
            Credentials = string.IsNullOrEmpty(user)
                ? CredentialCache.DefaultNetworkCredentials
                : new NetworkCredential(user, password),
        };

        using var message = new MailMessage(from, recipient, subject, htmlBody) { IsBodyHtml = true };
        await client.SendMailAsync(message);
    }
}
