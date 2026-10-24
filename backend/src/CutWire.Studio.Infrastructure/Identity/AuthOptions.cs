namespace CutWire.Studio.Infrastructure.Identity;

public sealed class AuthOptions
{
    public const string Section = "Auth";

    public string? BootstrapAdminEmail { get; set; }
    public string[] AllowedOrigins { get; set; } = [];
    public string? DataProtectionKeysPath { get; set; }
    public OpenIddictKeyOptions OpenIddict { get; set; } = new();
    public ExternalOptions External { get; set; } = new();
}

public sealed class OpenIddictKeyOptions
{
    public string? SigningCertificatePath { get; set; }
    public string? SigningCertificatePassword { get; set; }
    public string? EncryptionCertificatePath { get; set; }
    public string? EncryptionCertificatePassword { get; set; }
    public bool UseEphemeralKeys { get; set; }
}

public sealed class ExternalOptions
{
    public ProviderOptions GitHub { get; set; } = new();
    public ProviderOptions Google { get; set; } = new();
    public ProviderOptions Discord { get; set; } = new();
}

public sealed class ProviderOptions
{
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }

    public bool Enabled => !string.IsNullOrWhiteSpace(ClientId);
}
