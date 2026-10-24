using System.Net;
using CutWire.Studio.Application.Abstractions;

namespace CutWire.Studio.Infrastructure.Email;

public static class EmailTemplates
{
    public static EmailMessage VerifyEmail(string to, string link) => Build(to,
        "Verify your CutWire Studio email",
        "Welcome to CutWire Studio. Confirm your email address to finish signing up.",
        "Verify email", link);

    public static EmailMessage ResetPassword(string to, string link) => Build(to,
        "Reset your CutWire Studio password",
        "Someone asked to reset the password for your CutWire Studio account. If it was you, use the link below. If not, ignore this email.",
        "Reset password", link);

    public static EmailMessage ChangeEmail(string to, string link) => Build(to,
        "Confirm your new CutWire Studio email",
        "Confirm this address to use it for your CutWire Studio account.",
        "Confirm email", link);

    private static EmailMessage Build(string to, string subject, string intro, string action, string link)
    {
        var text = $"{intro}\n\n{action}: {link}\n\nThe link expires soon.";
        var html = $"<p>{WebUtility.HtmlEncode(intro)}</p><p><a href=\"{WebUtility.HtmlEncode(link)}\">{WebUtility.HtmlEncode(action)}</a></p>" +
                   $"<p>Or paste this into your browser:<br>{WebUtility.HtmlEncode(link)}</p><p>The link expires soon.</p>";
        return new EmailMessage(to, subject, html, text);
    }
}
