using CutWire.Studio.Application.Abstractions;
using CutWire.Studio.Application.Blobs;
using CutWire.Studio.Application.Quality;
using CutWire.Studio.Domain.Catalog;
using CutWire.Studio.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace CutWire.Studio.Application.Catalog;

public sealed record CreateItemCommand(
    Guid OwnerId, string? Kind, string? Title, string? Summary, string? Description,
    string? Category, IReadOnlyList<string>? Tags, string? License);

public sealed class CreateItemHandler(
    IAppDbContext db, IClock clock, IAssetKindRegistry kinds, ContentGatePipeline pipeline)
    : ICommandHandler<CreateItemCommand, ItemDetailDto>
{
    public async Task<Result<ItemDetailDto>> Handle(CreateItemCommand c, CancellationToken ct)
    {
        if (!AssetKindId.TryParse(c.Kind, out var kindId) || kinds.Get(kindId) is not { Enabled: true })
            return LoadReject.Unknown();

        var draft = new SubmissionDraft(kindId, c.OwnerId, c.Title ?? "", c.Summary ?? "", c.Description ?? "",
            c.Tags ?? [], c.Category ?? "", c.License ?? "", [], null);
        var blocking = (await pipeline.CheckMetadataAsync(draft, ct)).Where(v => v.Severity == GateSeverity.Block).ToList();
        if (blocking.Count > 0) return ContentGatePipeline.ToError(blocking);

        var now = clock.UtcNow;
        var slug = await UniqueSlugAsync(kindId, Slugs.From(draft.Title), ct);
        var item = Item.CreateDraft(Guid.CreateVersion7(), kindId, slug, c.OwnerId, draft.Title, draft.Summary, draft.Description,
            draft.License, draft.Category, now);
        item.SetTags(await db.Tags.Where(t => t.Kind == kindId && draft.Tags.Contains(t.Slug)).ToListAsync(ct), now);
        db.Items.Add(item);
        await db.SaveChangesAsync(ct);

        var created = await db.Items.WithAll().FirstAsync(i => i.Id == item.Id, ct);
        return ItemMapper.Detail(created, created.Owner);
    }

    private async Task<string> UniqueSlugAsync(AssetKindId kind, string baseSlug, CancellationToken ct)
    {
        var slug = baseSlug;
        for (var attempt = 0; attempt < 5 && await db.Items.AnyAsync(i => i.Kind == kind && i.Slug == slug, ct); attempt++)
            slug = $"{baseSlug}-{Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(3))}";
        return slug;
    }
}

internal static class LoadReject
{
    public static Error Unknown() => ItemLoader.Reject("kind", "unknown_kind", "Unknown or unavailable asset kind.");
}

public sealed record UpdateItemCommand(
    Guid ItemId, Guid UserId, string? Title, string? Summary, string? Description,
    string? Category, IReadOnlyList<string>? Tags, string? License);

public sealed class UpdateItemHandler(IAppDbContext db, IClock clock, ContentGatePipeline pipeline, IDuplicateLookup duplicates)
    : ICommandHandler<UpdateItemCommand, ItemDetailDto>
{
    public async Task<Result<ItemDetailDto>> Handle(UpdateItemCommand c, CancellationToken ct)
    {
        var loaded = await ItemLoader.LoadOwnedAsync(db, c.ItemId, c.UserId, ct);
        if (loaded.IsFailure) return loaded.Error;
        var item = loaded.Value;

        var title = c.Title ?? item.Title;
        var summary = c.Summary ?? item.Summary;
        var description = c.Description ?? item.Description;
        var category = c.Category ?? item.Category;
        var license = c.License ?? item.License;
        var tags = c.Tags ?? item.Tags.Select(t => t.Tag.Slug).ToList();

        if (item.Status != ItemStatus.Draft)
        {
            var draft = new SubmissionDraft(item.Kind, item.OwnerId, title, summary, description, tags, category, license, [], null, item.Id);
            var blocking = (await pipeline.CheckMetadataAsync(draft, ct)).Where(v => v.Severity == GateSeverity.Block).ToList();
            var normalized = TextRules.NormalizeTitle(title);
            if (normalized.Length > 0 && (await duplicates.OwnerTitlesAsync(item.OwnerId, item.Id, ct)).Any(t => TextRules.NormalizeTitle(t) == normalized))
                blocking.Add(new GateViolation("title", "duplicate_title", "You already have an item with this title."));
            if (blocking.Count > 0) return ContentGatePipeline.ToError(blocking);
        }

        var now = clock.UtcNow;
        item.UpdateMetadata(title, summary, description, category, license, now);
        if (c.Tags is not null)
            item.SetTags(await db.Tags.Where(t => t.Kind == item.Kind && tags.Contains(t.Slug)).ToListAsync(ct), now);
        await db.SaveChangesAsync(ct);
        return ItemMapper.Detail(item, item.Owner);
    }
}

public sealed record UploadPackageCommand(Guid ItemId, Guid UserId, Stream Content);

public sealed class UploadPackageHandler(
    IAppDbContext db, IClock clock, IAssetKindRegistry kinds, PackageIntake intake, BlobReferences refs)
    : ICommandHandler<UploadPackageCommand, ItemDetailDto>
{
    public async Task<Result<ItemDetailDto>> Handle(UploadPackageCommand c, CancellationToken ct)
    {
        var loaded = await ItemLoader.LoadOwnedAsync(db, c.ItemId, c.UserId, ct);
        if (loaded.IsFailure) return loaded.Error;
        var item = loaded.Value;
        if (item.Status != ItemStatus.Draft)
            return Error.Conflict("item.use_versions_endpoint", "Published items take new packages through POST /items/{id}/versions.");
        if (kinds.Get(item.Kind) is not { Enabled: true } kind) return LoadReject.Unknown();

        var uploaded = await intake.IntakeAsync(kind, c.Content, ct);
        if (uploaded.IsFailure) return uploaded.Error;
        var package = uploaded.Value;

        var now = clock.UtcNow;
        foreach (var old in item.Versions.ToList())
        {
            await refs.ReleaseAsync(old.PackageSha256, ct);
            item.RemoveVersion(old);
        }

        await refs.AddAsync(package.Hash, ct);
        item.AddVersion(new ItemVersion(Guid.CreateVersion7(), item.Id, package.Info.Version, "", package.Hash, package.Size,
            package.Info.Format, package.Info.MinAppVersion, package.Info.Manifest, now), now);
        item.SetKindMeta(ItemMapper.KindMeta(package.Info), package.Info.SearchText);
        await db.SaveChangesAsync(ct);
        return ItemMapper.Detail(item, item.Owner);
    }
}

public sealed record UploadMediaCommand(Guid ItemId, Guid UserId, string Role, Stream Content);

public sealed class UploadMediaHandler(
    IAppDbContext db, IAssetKindRegistry kinds, IImageAnalyzer images, ContentGatePipeline pipeline,
    ICommandHandler<StoreBlobCommand, StoreBlobResult> storeBlob, BlobReferences refs,
    IDuplicateLookup duplicates, ContentRulesOptions rules, IClock clock)
    : ICommandHandler<UploadMediaCommand, ItemDetailDto>
{
    public async Task<Result<ItemDetailDto>> Handle(UploadMediaCommand c, CancellationToken ct)
    {
        var loaded = await ItemLoader.LoadOwnedAsync(db, c.ItemId, c.UserId, ct);
        if (loaded.IsFailure) return loaded.Error;
        var item = loaded.Value;
        if (item.Status is not (ItemStatus.Draft or ItemStatus.Published))
            return Error.Conflict("item.not_editable", "Media can only be replaced on drafts and published items.");
        if (!MediaRoleExtensions.TryParse(c.Role, out var role))
            return ItemLoader.Reject($"media.{c.Role}", "media_role_not_allowed", $"Unknown media role '{c.Role}'.");
        if (kinds.Get(item.Kind) is not { Enabled: true } kind) return LoadReject.Unknown();

        var spec = kind.MediaSpecs.FirstOrDefault(s => s.Role == role);
        var data = await BoundedRead.ReadAsync(c.Content, (spec?.MaxBytes ?? 0) + 1L, ct);
        var upload = new MediaUpload(role, data, MediaSpec.MimeType);
        var draft = new SubmissionDraft(item.Kind, item.OwnerId, item.Title, item.Summary, item.Description, [], item.Category, item.License, [upload], null, item.Id);
        var violations = pipeline.CheckMedia(draft, upload);
        if (violations.Count > 0) return ContentGatePipeline.ToError(violations);

        var probe = images.Probe(data)!;
        var stored = await storeBlob.Handle(new StoreBlobCommand(new MemoryStream(data), MediaSpec.MimeType, spec!.MaxBytes), ct);
        if (stored.IsFailure) return stored.Error;

        foreach (var old in item.Media.Where(m => m.Role == role).ToList())
        {
            await refs.ReleaseAsync(old.BlobSha256, ct);
            item.RemoveMedia(old);
        }

        // Publish runs the similarity flag for drafts; a published item swapping its thumbnail would otherwise skip it.
        if (item.Status == ItemStatus.Published && role == MediaRole.Thumbnail
            && await duplicates.SimilarThumbnailExistsAsync(probe.DHash, rules.SimilarThumbnailMaxDistance, item.Id, ct))
        {
            var parameters = System.Text.Json.JsonSerializer.SerializeToElement(new Dictionary<string, object> { ["maxDistance"] = rules.SimilarThumbnailMaxDistance });
            item.AddFlag(new ItemFlag(Guid.CreateVersion7(), item.Id, "media.thumbnail", "similar_thumbnail",
                "The thumbnail looks very similar to an existing item.", parameters, clock.UtcNow));
        }

        await refs.AddAsync(stored.Value.Hash, ct);
        item.AddMedia(new ItemMedia(Guid.CreateVersion7(), item.Id, role, stored.Value.Hash, probe.Width, probe.Height, data.Length, probe.DHash));
        await db.SaveChangesAsync(ct);
        return ItemMapper.Detail(item, item.Owner);
    }
}

public sealed record PublishItemCommand(Guid ItemId, Guid UserId);

public sealed class PublishItemHandler(
    IAppDbContext db, IClock clock, IAssetKindRegistry kinds, IBlobStore store, ContentGatePipeline pipeline)
    : ICommandHandler<PublishItemCommand, ItemDetailDto>
{
    public async Task<Result<ItemDetailDto>> Handle(PublishItemCommand c, CancellationToken ct)
    {
        var loaded = await ItemLoader.LoadOwnedAsync(db, c.ItemId, c.UserId, ct);
        if (loaded.IsFailure) return loaded.Error;
        var item = loaded.Value;
        if (item.Status != ItemStatus.Draft) return Error.Conflict("item.not_draft", "Only drafts can be published.");
        if (kinds.Get(item.Kind) is not { Enabled: true } kind) return LoadReject.Unknown();

        var version = item.Versions.FirstOrDefault();
        if (version is null) return ItemLoader.Reject("package", "package_missing", "Upload a package before publishing.");

        await using var packageStream = await store.OpenAsync(version.PackageSha256, ct: ct);
        if (packageStream is null) return Error.Unavailable("package.unavailable", "The stored package is missing.");
        var validated = await kind.ValidateAsync(packageStream, ct);
        if (validated.IsFailure) return validated.Error;

        var media = new List<MediaUpload>();
        foreach (var m in item.Media)
        {
            await using var stream = await store.OpenAsync(m.BlobSha256, ct: ct);
            if (stream is null) continue;
            media.Add(new MediaUpload(m.Role, await BoundedRead.ReadAsync(stream, int.MaxValue, ct), MediaSpec.MimeType));
        }

        var draft = new SubmissionDraft(item.Kind, item.OwnerId, item.Title, item.Summary, item.Description,
            item.Tags.Select(t => t.Tag.Slug).ToList(), item.Category, item.License, media, validated.Value, item.Id);
        var report = await pipeline.ValidateAsync(draft, ct);
        if (report.IsFailure) return report.Error;

        var now = clock.UtcNow;
        foreach (var flag in report.Value.Flags)
        {
            var parameters = System.Text.Json.JsonSerializer.SerializeToElement(flag.Params ?? new Dictionary<string, object>());
            item.AddFlag(new ItemFlag(Guid.CreateVersion7(), item.Id, flag.Field, flag.Code, flag.Message, parameters, now));
        }

        item.Publish(version, now);
        await db.SaveChangesAsync(ct);
        return ItemMapper.Detail(item, item.Owner);
    }
}

public sealed record UploadVersionCommand(Guid ItemId, Guid UserId, Stream Content, string? Changelog);

public sealed class UploadVersionHandler(
    IAppDbContext db, IClock clock, IAssetKindRegistry kinds, PackageIntake intake, BlobReferences refs, IDuplicateLookup duplicates)
    : ICommandHandler<UploadVersionCommand, ItemDetailDto>
{
    public const int MaxChangelogLength = 2000;

    public async Task<Result<ItemDetailDto>> Handle(UploadVersionCommand c, CancellationToken ct)
    {
        var loaded = await ItemLoader.LoadOwnedAsync(db, c.ItemId, c.UserId, ct);
        if (loaded.IsFailure) return loaded.Error;
        var item = loaded.Value;
        if (item.Status != ItemStatus.Published)
            return Error.Conflict("item.not_published", "New versions can only be added to published items.");
        if (kinds.Get(item.Kind) is not { Enabled: true } kind) return LoadReject.Unknown();

        var changelog = (c.Changelog ?? "").Trim();
        if (changelog.Length > MaxChangelogLength)
            return ItemLoader.Reject("changelog", "too_long", $"Changelog must be at most {MaxChangelogLength} characters.", ("max", MaxChangelogLength));
        if (changelog.Any(ch => ch != '\n' && (ch < ' ' || ch > '~')))
            return ItemLoader.Reject("changelog", "non_ascii", "Changelog may only use printable ASCII characters.");

        var uploaded = await intake.IntakeAsync(kind, c.Content, ct);
        if (uploaded.IsFailure) return uploaded.Error;
        var package = uploaded.Value;

        SemVersion.TryParse(package.Info.Version, out var next);
        var latest = item.Versions.Select(v => SemVersion.TryParse(v.Version, out var s) ? s : null).OfType<SemVersion>().Max();
        if (next is null || (latest is not null && next.CompareTo(latest) <= 0))
            return ItemLoader.Reject("package", "version_not_increased", "The package version must be higher than every existing version.",
                ("version", package.Info.Version), ("latest", item.Versions.OrderBy(v => v.CreatedAt).Last().Version));
        if (await duplicates.PackageExistsAsync(package.Hash, null, ct))
            return ItemLoader.Reject("package", "duplicate_package", "This exact package has already been uploaded.");

        var now = clock.UtcNow;
        await refs.AddAsync(package.Hash, ct);
        var version = item.AddVersion(new ItemVersion(Guid.CreateVersion7(), item.Id, package.Info.Version, changelog, package.Hash,
            package.Size, package.Info.Format, package.Info.MinAppVersion, package.Info.Manifest, now), now);
        item.SetCurrentVersion(version, now);
        item.SetKindMeta(ItemMapper.KindMeta(package.Info), package.Info.SearchText);
        await db.SaveChangesAsync(ct);
        return ItemMapper.Detail(item, item.Owner);
    }
}

public sealed record DeleteItemCommand(Guid ItemId, Guid UserId);

public sealed class DeleteItemHandler(IAppDbContext db, IClock clock, BlobReferences refs)
    : ICommandHandler<DeleteItemCommand, Guid>
{
    public async Task<Result<Guid>> Handle(DeleteItemCommand c, CancellationToken ct)
    {
        var loaded = await ItemLoader.LoadOwnedAsync(db, c.ItemId, c.UserId, ct);
        if (loaded.IsFailure) return loaded.Error;
        var item = loaded.Value;

        foreach (var version in item.Versions) await refs.ReleaseAsync(version.PackageSha256, ct);
        foreach (var media in item.Media) await refs.ReleaseAsync(media.BlobSha256, ct);
        item.MarkRemoved(clock.UtcNow);
        await db.SaveChangesAsync(ct);
        return item.Id;
    }
}
