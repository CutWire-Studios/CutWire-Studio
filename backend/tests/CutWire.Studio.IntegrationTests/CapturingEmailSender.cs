using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using CutWire.Studio.Application.Abstractions;

namespace CutWire.Studio.IntegrationTests;

public sealed partial class CapturingEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> _sent = new();

    public IReadOnlyCollection<EmailMessage> Sent => _sent;

    public Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        _sent.Enqueue(message);
        return Task.CompletedTask;
    }

    public EmailMessage Last(string to) => _sent.Last(m => m.To == to);

    public (string UserId, string Token) VerificationLink(string to)
    {
        var text = Last(to).Text;
        return (Param(text, "userId"), Param(text, "token"));
    }

    public static string Param(string text, string name)
    {
        var match = Regex.Match(text, $@"[?&]{name}=([^&\s]+)");
        return Uri.UnescapeDataString(match.Groups[1].Value);
    }
}
