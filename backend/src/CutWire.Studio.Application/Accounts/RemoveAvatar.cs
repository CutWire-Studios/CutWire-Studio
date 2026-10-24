using CutWire.Studio.Application.Abstractions;
using CutWire.Studio.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace CutWire.Studio.Application.Accounts;

public sealed record RemoveAvatarCommand(Guid UserId);

public sealed class RemoveAvatarHandler(IAppDbContext db, IClock clock)
    : ICommandHandler<RemoveAvatarCommand, ProfileDto>
{
    public async Task<Result<ProfileDto>> Handle(RemoveAvatarCommand command, CancellationToken ct)
    {
        var profile = await db.UserProfiles.FirstOrDefaultAsync(p => p.Id == command.UserId, ct);
        if (profile is null) return Error.NotFound("profile.not_found", "Profile not found.");

        if (profile.SetAvatar(null) is { } previous)
        {
            var blob = await db.Blobs.FirstOrDefaultAsync(b => b.Id == previous, ct);
            blob?.Release(clock.UtcNow);
            await db.SaveChangesAsync(ct);
        }

        return ProfileDto.From(profile, clock.UtcNow);
    }
}
