using System.ComponentModel.DataAnnotations;

namespace CutWire.Studio.Infrastructure.Blobs;

public sealed class BlobStoreOptions
{
    public const string Section = "Storage";

    [Required]
    public string BlobRoot { get; set; } = "";
}
