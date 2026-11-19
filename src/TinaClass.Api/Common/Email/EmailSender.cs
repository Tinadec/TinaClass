using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;

namespace TinaClass.Api.Common.Email;

public sealed record EmailMessage(string To, string Subject, string Body);

/// <summary>
/// Sends email. Nothing in the scaffold sends any yet; password reset codes and invitations (copy them from Atlas) do.
/// <c>Email:Mode</c> is <c>Log</c> (default) or <c>Smtp</c>.
/// </summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

public sealed class EmailOptions
{
    /// <summary><c>Log</c> writes messages to the log (development); <c>Smtp</c> sends them.</summary>
    public string Mode { get; set; } = "Log";

    public string From { get; set; } = "no-reply@example.com";

    public string? SmtpHost { get; set; }

    public int SmtpPort { get; set; } = 587;

    public bool SmtpSsl { get; set; } = true;

    public string? SmtpUser { get; set; }

    public string? SmtpPassword { get; set; }
}

/// <summary>
/// Writes each message to the log instead of sending it. A body can hold a code or a link, so it is logged in Development only;
/// elsewhere the log shows who and what, never the text.
/// </summary>
internal sealed partial class LogEmailSender(ILogger<LogEmailSender> logger, IHostEnvironment environment) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        if (environment.IsDevelopment())
        {
            LogWithBody(logger, message.To, message.Subject, message.Body);
        }
        else
        {
            LogWithoutBody(logger, message.To, message.Subject);
        }
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Email to {To}: {Subject}\n{Body}")]
    private static partial void LogWithBody(ILogger logger, string to, string subject, string body);

    [LoggerMessage(Level = LogLevel.Information, Message = "Email to {To}: {Subject} (the text is logged in Development only)")]
    private static partial void LogWithoutBody(ILogger logger, string to, string subject);
}

internal sealed class SmtpEmailSender(IOptions<EmailOptions> options) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var o = options.Value;
        using var client = new SmtpClient(o.SmtpHost, o.SmtpPort) { EnableSsl = o.SmtpSsl };
        if (o.SmtpUser is not null)
        {
            client.Credentials = new NetworkCredential(o.SmtpUser, o.SmtpPassword);
        }
        using var mail = new MailMessage(o.From, message.To, message.Subject, message.Body);
        await client.SendMailAsync(mail, cancellationToken);
    }
}
