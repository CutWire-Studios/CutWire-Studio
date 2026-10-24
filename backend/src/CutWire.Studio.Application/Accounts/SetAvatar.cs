using CutWire.Studio.Application.Abstractions;
using CutWire.Studio.Application.Blobs;
using CutWire.Studio.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace CutWire.Studio.Application.Accounts;

public sealed record SetAvatarCommand(Guid UserId, Stream Content);

public sealed class SetAvatarHandler(
    IAppDbContext db,
    IClock clock,
    IImageInspector inspector,
    ICommandHandler<StoreBlobCommand, StoreBlobResult> storeBlob) : ICommandHandler<SetAvatarCommand, ProfileDto>
{
    public const long MaxBytes = 256 * 1024;
    public const int MaxDimension = 512;

    private static readonly string[] AllowedMimes = ["image/png", "image/jpeg", "image/webp"];

    public async Task<Result<ProfileDto>> Handle(SetAvatarCommand command, CancellationToken ct)
    {
        var profile = await db.UserProfiles.FirstOrDefaultAsync(p => p.Id == command.UserId, ct);
        if (profile is null) return Error.NotFound("profile.not_found", "Profile not found.");

        if (command.Content.Length > MaxBytes)
            return Error.TooLarge("avatar.too_large", $"Avatar must be at most {MaxBytes / 1024} KB.");

        var info = inspector.Inspect(command.Content);
        if (info is null || !AllowedMimes.Contains(info.Mime))
            return Error.Validation("avatar.invalid_image", "Avatar must be a WebP, PNG or JPEG image.");
        if (info.Width > MaxDimension || info.Height > MaxDimension)
            return Error.Validation("avatar.too_large_dimensions",
                $"Avatar must be at most {MaxDimension}x{MaxDimension} pixels.");

        command.Content.Position = 0;
        var stored = await storeBlob.Handle(new StoreBlobCommand(command.Content, info.Mime, MaxBytes), ct);
        if (stored.IsFailure) return stored.Error;

        if (profile.AvatarSha256 != stored.Value.Hash)
        {
            var blob = await db.Blobs.FirstAsync(b => b.Id == stored.Value.Hash, ct);
            blob.AddRef();
            await ReleaseAsync(profile.SetAvatar(blob.Id), ct);
            await db.SaveChangesAsync(ct);
        }

        return ProfileDto.From(profile, clock.UtcNow);
    }

    private async Task ReleaseAsync(Domain.Blobs.Sha256Hash? previous, CancellationToken ct)
    {
        if (previous is not { } hash) return;
        var old = await db.Blobs.FirstOrDefaultAsync(b => b.Id == hash, ct);
        old?.Release(clock.UtcNow);
    }
}
