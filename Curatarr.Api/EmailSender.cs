namespace Curatarr.Api;

public interface IAppEmailSender
{
    Task SendAsync(string toEmail, string subject, string bodyText);
}

/// <summary>
/// Dev-mode email sender: logs the message (including any link) to the
/// API console instead of actually sending it. Lets the whole invite /
/// password-reset flow be tested end-to-end without SMTP credentials --
/// copy the link from the terminal and paste it into a browser. Used
/// automatically when Smtp:Host is not configured.
/// </summary>
public class ConsoleEmailSender : IAppEmailSender
{
    private readonly ILogger<ConsoleEmailSender> _logger;
    public ConsoleEmailSender(ILogger<ConsoleEmailSender> logger) => _logger = logger;

    public Task SendAsync(string toEmail, string subject, string bodyText)
    {
        _logger.LogInformation(
            "\n========== EMAIL (dev mode -- not actually sent) ==========\nTo: {To}\nSubject: {Subject}\n\n{Body}\n=============================================================",
            toEmail, subject, bodyText);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Real SMTP-backed sender (Gmail app password, or any standard SMTP
/// relay). Configured entirely via environment variables:
///   Smtp__Host     e.g. smtp.gmail.com
///   Smtp__Port     e.g. 587 (STARTTLS)
///   Smtp__User     SMTP login (often the full email address)
///   Smtp__Password app password / SMTP password
///   Smtp__From     sender address shown to recipients
///   Smtp__EnableSsl (optional, default true)
/// Registered in Program.cs only when Smtp__Host is set; otherwise the
/// console fallback stays active.
/// </summary>
public class SmtpEmailSender : IAppEmailSender
{
    private readonly IConfiguration _config;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(IConfiguration config, ILogger<SmtpEmailSender> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task SendAsync(string toEmail, string subject, string bodyText)
    {
        var host = _config["Smtp:Host"]
            ?? throw new InvalidOperationException("Smtp:Host is not configured.");
        var port = int.TryParse(_config["Smtp:Port"], out var p) ? p : 587;
        var user = _config["Smtp:User"];
        var password = _config["Smtp:Password"];
        var from = _config["Smtp:From"] ?? user
            ?? throw new InvalidOperationException("Smtp:From (or Smtp:User) must be configured.");
        var enableSsl = !bool.TryParse(_config["Smtp:EnableSsl"], out var ssl) || ssl;

        using var message = new System.Net.Mail.MailMessage(from, toEmail, subject, bodyText);
        using var client = new System.Net.Mail.SmtpClient(host, port)
        {
            EnableSsl = enableSsl,
            DeliveryMethod = System.Net.Mail.SmtpDeliveryMethod.Network,
        };
        if (!string.IsNullOrWhiteSpace(user))
            client.Credentials = new System.Net.NetworkCredential(user, password);

        // Throws on auth/network failure -- callers (invite rollback,
        // forgot-password logging) handle it. Never log the password.
        await client.SendMailAsync(message);
        _logger.LogInformation("[Email] Sent '{Subject}' to {To} via {Host}:{Port}",
            subject, toEmail, host, port);
    }
}
