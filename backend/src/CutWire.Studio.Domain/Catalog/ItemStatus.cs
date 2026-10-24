namespace CutWire.Studio.Domain.Catalog;

public enum ItemStatus
{
    Draft,
    Published,
    Hidden,
    Removed,
}

public static class ItemStatusExtensions
{
    public static string ToKey(this ItemStatus status) => status.ToString().ToLowerInvariant();

    public static bool TryParse(string? key, out ItemStatus status) =>
        Enum.TryParse(key, ignoreCase: true, out status) && Enum.IsDefined(status);
}
