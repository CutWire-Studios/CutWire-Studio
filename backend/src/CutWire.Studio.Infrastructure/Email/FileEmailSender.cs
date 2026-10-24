using CutWire.Studio.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CutWire.Studio.Infrastructure.Email;

public sealed class FileEmailSender(IOptions<EmailOptions> options, ILogger<FileEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        var directory = Path.GetFullPath(options.Value.DevDirectory);
        Directory.CreateDirectory(directory);
        var safeTo = new string(message.To.Select(c => char.IsAsciiLetterOrDigit(c) ? c : '_').ToArray());
        var path = Path.Combine(directory, $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{safeTo}-{Guid.NewGuid():N}.eml");
        await using (var stream = File.Create(path))
            await MimeFactory.Create(options.Value.Smtp.From, message).WriteToAsync(stream, ct);
        logger.LogInformation("Email to {To}: {Subject} ({Path})\n{Text}", message.To, message.Subject, path, message.Text);
    }
}
