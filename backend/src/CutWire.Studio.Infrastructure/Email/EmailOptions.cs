namespace CutWire.Studio.Infrastructure.Email;

public sealed class EmailOptions
{
    public const string Section = "Email";

    public SmtpOptions Smtp { get; set; } = new();
    public string DevDirectory { get; set; } = "data/mail";
}

public sealed class SmtpOptions
{
    public string? Host { get; set; }
    public int Port { get; set; } = 465;
    public string? User { get; set; }
    public string? Password { get; set; }
    public string From { get; set; } = "no-reply@cutwire.studio";
}
