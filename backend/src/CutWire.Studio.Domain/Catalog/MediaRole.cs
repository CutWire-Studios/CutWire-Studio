namespace CutWire.Studio.Domain.Catalog;

public enum MediaRole
{
    Thumbnail,
    PreviewStrip,
    Screenshot,
    Gallery,
}

public static class MediaRoleExtensions
{
    public static string ToKey(this MediaRole role) => role switch
    {
        MediaRole.Thumbnail => "thumbnail",
        MediaRole.PreviewStrip => "preview-strip",
        MediaRole.Screenshot => "screenshot",
        MediaRole.Gallery => "gallery",
        _ => throw new ArgumentOutOfRangeException(nameof(role)),
    };

    public static bool TryParse(string? key, out MediaRole role)
    {
        foreach (var candidate in Enum.GetValues<MediaRole>())
            if (candidate.ToKey() == key)
            {
                role = candidate;
                return true;
            }

        role = default;
        return false;
    }
}
