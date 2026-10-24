namespace CutWire.Studio.Domain.Catalog;

public sealed class Tag
{
    private Tag() { }

    public Tag(AssetKindId kind, string slug)
    {
        Kind = kind;
        Slug = slug;
    }

    public int Id { get; private set; }
    public AssetKindId Kind { get; private set; }
    public string Slug { get; private set; } = null!;
}

public sealed class ItemTag
{
    private ItemTag() { }

    public ItemTag(Guid itemId, Tag tag)
    {
        ItemId = itemId;
        TagId = tag.Id;
        Tag = tag;
    }

    public Guid ItemId { get; private set; }
    public int TagId { get; private set; }
    public Tag Tag { get; private set; } = null!;
}
