using System.Text.Json;
using CutWire.Studio.Domain.Accounts;
using CutWire.Studio.Domain.Common;

namespace CutWire.Studio.Domain.Catalog;

public sealed class Item : Entity<Guid>
{
    public const int MaxSlugLength = 80;

    private Item() { }

    private readonly List<ItemVersion> _versions = [];
    private readonly List<ItemMedia> _media = [];
    private readonly List<ItemTag> _tags = [];
    private readonly List<ItemFlag> _flags = [];

    public static Item CreateDraft(Guid id, AssetKindId kind, string slug, Guid ownerId, string title, string summary,
        string description, string license, string category, DateTimeOffset now) => new()
    {
        Id = id,
        Kind = kind,
        Slug = slug,
        OwnerId = ownerId,
        Title = title,
        Summary = summary,
        Description = description,
        License = license,
        Category = category,
        Status = ItemStatus.Draft,
        KindMeta = EmptyObject,
        CreatedAt = now,
        UpdatedAt = now,
    };

    public static readonly JsonElement EmptyObject = JsonDocument.Parse("{}").RootElement.Clone();

    public AssetKindId Kind { get; private set; }
    public string Slug { get; private set; } = null!;
    public Guid OwnerId { get; private set; }
    public UserProfile Owner { get; private set; } = null!;
    public string Title { get; private set; } = null!;
    public string Summary { get; private set; } = null!;
    public string Description { get; private set; } = null!;
    public string License { get; private set; } = null!;
    public string Category { get; private set; } = null!;
    public ItemStatus Status { get; private set; }
    public string? VisibilityReason { get; private set; }
    public double ScoreUp { get; private set; }
    public double ScoreDown { get; private set; }
    public double WilsonScore { get; private set; }
    public double HotScore { get; private set; }
    public long DownloadsTotal { get; private set; }
    public JsonElement KindMeta { get; private set; }
    public string SearchTags { get; private set; } = "";
    public string SearchExtra { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? PublishedAt { get; private set; }
    public Guid? CurrentVersionId { get; private set; }

    public IReadOnlyList<ItemVersion> Versions => _versions;
    public IReadOnlyList<ItemMedia> Media => _media;
    public IReadOnlyList<ItemTag> Tags => _tags;
    public IReadOnlyList<ItemFlag> Flags => _flags;

    public void UpdateMetadata(string title, string summary, string description, string category, string license, DateTimeOffset now)
    {
        Title = title;
        Summary = summary;
        Description = description;
        Category = category;
        License = license;
        UpdatedAt = now;
    }

    public void SetTags(IReadOnlyCollection<Tag> tags, DateTimeOffset now)
    {
        _tags.RemoveAll(t => tags.All(n => n.Id != t.TagId));
        foreach (var tag in tags.Where(n => _tags.All(t => t.TagId != n.Id)))
            _tags.Add(new ItemTag(Id, tag));
        SearchTags = string.Join(' ', tags.Select(t => t.Slug.Replace('-', ' ')).Order(StringComparer.Ordinal));
        UpdatedAt = now;
    }

    public void SetKindMeta(JsonElement meta, string searchExtra)
    {
        KindMeta = meta.Clone();
        SearchExtra = searchExtra.Length > 2000 ? searchExtra[..2000] : searchExtra;
    }

    public ItemVersion AddVersion(ItemVersion version, DateTimeOffset now)
    {
        _versions.Add(version);
        UpdatedAt = now;
        return version;
    }

    public void RemoveVersion(ItemVersion version) => _versions.Remove(version);

    public ItemMedia AddMedia(ItemMedia media)
    {
        _media.Add(media);
        return media;
    }

    public void RemoveMedia(ItemMedia media) => _media.Remove(media);

    public void AddFlag(ItemFlag flag) => _flags.Add(flag);

    public void Publish(ItemVersion version, DateTimeOffset now)
    {
        Status = ItemStatus.Published;
        VisibilityReason = null;
        PublishedAt = now;
        UpdatedAt = now;
        CurrentVersionId = version.Id;
        RecomputeScores(now);
    }

    public void SetCurrentVersion(ItemVersion version, DateTimeOffset now)
    {
        CurrentVersionId = version.Id;
        UpdatedAt = now;
    }

    public void MarkRemoved(DateTimeOffset now)
    {
        Status = ItemStatus.Removed;
        CurrentVersionId = null;
        _versions.Clear();
        _media.Clear();
        UpdatedAt = now;
    }

    /// <summary>Recomputes <see cref="WilsonScore"/> and <see cref="HotScore"/> from the vote tallies. Call after any vote change.</summary>
    public void RecomputeScores(DateTimeOffset now)
    {
        WilsonScore = Ranking.Wilson(ScoreUp, ScoreDown);
        HotScore = PublishedAt is { } published ? Ranking.Hot(WilsonScore, published, now) : 0;
    }

    public bool IsPublic => Status == ItemStatus.Published;
}
