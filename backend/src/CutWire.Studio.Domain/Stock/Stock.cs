using CutWire.Studio.Domain.Blobs;

namespace CutWire.Studio.Domain.Stock;

public static class JobStatus
{
    public const string Queued = "queued";
    public const string Processing = "processing";
    public const string Ready = "ready";
    public const string Failed = "failed";
}

public static class QuotaKind
{
    public const string Consumed = "consumed";
    public const string Refunded = "refunded";
}

/// <summary>Operator-tunable enablement, order and download quota for one (provider, type). Seeded from the provider's defaults, never overwritten.</summary>
public sealed class ProviderSetting
{
    public string Provider { get; set; } = null!;
    public string Type { get; set; } = null!;
    public bool Enabled { get; set; } = true;
    public string? Label { get; set; }
    public int Sort { get; set; }
    public int QuotaLimit { get; set; }
    public string QuotaWindow { get; set; } = "day";
    public string QuotaScope { get; set; } = "client";

    /// <summary>Distributions (<see cref="Distributions.All"/>) that must not see this source, e.g. android-play for store moderation.</summary>
    public List<string> HiddenFor { get; set; } = [];

    public bool VisibleTo(string? distribution) => distribution is null || !HiddenFor.Contains(distribution);
}

/// <summary>
/// The package types Drift reports in X-Cutwire-Distribution. "source" is a local or third-party build; "unknown" is any client that sends no
/// header, which includes builds from before it existed. Hiding is for store moderation, not access control: the header is unsigned.
/// </summary>
public static class Distributions
{
    public const string Header = "X-Cutwire-Distribution";
    public const string Unknown = "unknown";

    public static readonly string[] All =
    [
        "windows", "windows-store", "macos", "appimage", "flatpak", "flathub", "arch", "android-github", "android-play", "source", Unknown,
    ];
}

public sealed class DownloadJob
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string ClientId { get; set; } = null!;
    public Guid? UserId { get; set; }
    public string Provider { get; set; } = null!;
    public string Type { get; set; } = null!;
    public string Native { get; set; } = null!;
    public string? VariantId { get; set; }
    public string Status { get; set; } = JobStatus.Queued;
    public double Progress { get; set; }
    public string QuotaSubject { get; set; } = null!;
    public string QuotaWindow { get; set; } = "day";
    public Sha256Hash? BlobSha256 { get; set; }
    public string? Filename { get; set; }
    public string? Mime { get; set; }
    public long Size { get; set; }
    public string? FileSha256 { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public string? LicenseName { get; set; }
    public string? LicenseUrl { get; set; }
    public string? Attribution { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorDetail { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Append-only quota ledger: a refund is a row, so a failed job cannot refund twice and usage never drifts.</summary>
public sealed class QuotaEvent
{
    public long Id { get; set; }
    public string Subject { get; set; } = null!;
    public string Provider { get; set; } = null!;
    public DateTimeOffset WindowStart { get; set; }
    public string Kind { get; set; } = null!;
    public Guid? JobId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Per-category override for the Drift Assets pack (<c>asset_categories</c>).</summary>
public sealed class AssetCategory
{
    public string Category { get; set; } = null!;
    public bool Enabled { get; set; } = true;
    public string? Label { get; set; }
    public int Sort { get; set; }
}

/// <summary>Per-category override for the SFX library (<c>sfx_categories</c>).</summary>
public sealed class SfxCategory
{
    public string Category { get; set; } = null!;
    public bool Enabled { get; set; } = true;
    public string? Label { get; set; }
    public int Sort { get; set; }
}

public sealed class ProviderCacheEntry
{
    public string Key { get; set; } = null!;
    public string Body { get; set; } = null!;
    public DateTimeOffset ExpiresAt { get; set; }
}

/// <summary>One mirrored Drift Assets release. <c>FilesJson</c> maps relative path to the blob sha256 holding it.</summary>
public sealed class DriftAssetPack
{
    public string Version { get; set; } = null!;
    public string IndexJson { get; set; } = null!;
    public string FilesJson { get; set; } = null!;
    public bool IsCurrent { get; set; }
    public DateTimeOffset SyncedAt { get; set; }
}
