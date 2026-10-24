using CutWire.Studio.Application.Abstractions;
using MimeKit;

namespace CutWire.Studio.Infrastructure.Email;

internal static class MimeFactory
{
    public static MimeMessage Create(string from, EmailMessage message)
    {
        var mime = new MimeMessage();
        mime.From.Add(MailboxAddress.Parse(from));
        mime.To.Add(MailboxAddress.Parse(message.To));
        mime.Subject = message.Subject;
        mime.Body = new BodyBuilder { HtmlBody = message.Html, TextBody = message.Text }.ToMessageBody();
        return mime;
    }
}
