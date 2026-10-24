using System.Text.Json;
using CutWire.Studio.Domain.Blobs;
using CutWire.Studio.Domain.Catalog;

namespace CutWire.Studio.Application.Catalog;

public sealed record PackageFileEntry(string Path, long Size, Sha256Hash Sha256);

public sealed record PackageInfo(
    PackageFormat Format,
    JsonElement Manifest,
    string PackageId,
    string Version,
    string MinAppVersion,
    string DriftInstallKind,
    IReadOnlyList<PackageFileEntry> Files,
    string SearchText,
    Sha256Hash PackageSha256);
