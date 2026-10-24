using CutWire.Studio.Application.Abstractions;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace CutWire.Studio.Infrastructure.Email;

public sealed class SmtpEmailSender(IOptions<EmailOptions> options) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        var smtp = options.Value.Smtp;
        using var client = new SmtpClient();
        await client.ConnectAsync(smtp.Host!, smtp.Port,
            smtp.Port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls, ct);
        if (!string.IsNullOrEmpty(smtp.User)) await client.AuthenticateAsync(smtp.User, smtp.Password ?? "", ct);
        await client.SendAsync(MimeFactory.Create(smtp.From, message), ct);
        await client.DisconnectAsync(true, ct);
    }
}
