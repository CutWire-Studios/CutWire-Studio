using CutWire.Studio.Domain.Catalog;

namespace CutWire.Studio.Domain.Marketplace;

/// <summary>
/// Targeting shared by layouts and flags. An empty distribution list targets every distribution. The version range is [min, max): max is exclusive.
/// A caller without a version (the web app, or a client whose header did not parse) is treated as inside any range, since there is nothing to compare.
/// </summary>
public static class Targeting
{
    public static bool Matches(IReadOnlyCollection<string> distributions, string? minAppVersion, string? maxAppVersion, string distribution, SemVersion? version)
    {
        if (distributions.Count > 0 && !distributions.Contains(distribution)) return false;
        if (version is null) return true;
        if (SemVersion.TryParse(minAppVersion, out var min) && version.CompareTo(min) < 0) return false;
        if (SemVersion.TryParse(maxAppVersion, out var max) && version.CompareTo(max) >= 0) return false;
        return true;
    }

    /// <summary>Higher is more specific: a distribution list and each bound of the version range narrow the target.</summary>
    public static int Specificity(IReadOnlyCollection<string> distributions, string? minAppVersion, string? maxAppVersion) =>
        (distributions.Count > 0 ? 4 : 0) + (minAppVersion is not null ? 1 : 0) + (maxAppVersion is not null ? 1 : 0);
}

/// <summary>A server-driven page layout. <see cref="PagesJson"/> is the validated <c>pages</c> array of the manifest.</summary>
public sealed class MarketplaceLayout
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Name { get; set; } = null!;
    public int Priority { get; set; }
    public bool Enabled { get; set; } = true;
    public List<string> Distributions { get; set; } = [];
    public string? MinAppVersion { get; set; }

    /// <summary>Exclusive upper bound.</summary>
    public string? MaxAppVersion { get; set; }

    public string PagesJson { get; set; } = "[]";
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public bool Matches(string distribution, SemVersion? version) =>
        Enabled && Targeting.Matches(Distributions, MinAppVersion, MaxAppVersion, distribution, version);
}

/// <summary>A remote switch or value. <see cref="ValueJson"/>, when set, is the flag's value; otherwise the value is <see cref="Enabled"/>.</summary>
public sealed class FeatureFlag
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Key { get; set; } = null!;
    public bool Enabled { get; set; } = true;
    public string? ValueJson { get; set; }
    public List<string> Distributions { get; set; } = [];
    public string? MinAppVersion { get; set; }
    public string? MaxAppVersion { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public bool Matches(string distribution, SemVersion? version) =>
        Targeting.Matches(Distributions, MinAppVersion, MaxAppVersion, distribution, version);
}
