using CutWire.Studio.Application.Abstractions;
using CutWire.Studio.Domain.Accounts;
using CutWire.Studio.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace CutWire.Studio.Application.Accounts;

public sealed record UpdateProfileCommand(Guid UserId, string? Handle, string? DisplayName, string? Bio);

public sealed class UpdateProfileHandler(IAppDbContext db, IClock clock)
    : ICommandHandler<UpdateProfileCommand, ProfileDto>
{
    public async Task<Result<ProfileDto>> Handle(UpdateProfileCommand command, CancellationToken ct)
    {
        var profile = await db.UserProfiles.FirstOrDefaultAsync(p => p.Id == command.UserId, ct);
        if (profile is null) return Error.NotFound("profile.not_found", "Profile not found.");

        var handle = UserHandle.Normalize(command.Handle);
        if (handle.IsFailure) return handle.Error;

        var displayName = command.DisplayName?.Trim() ?? "";
        var bio = command.Bio?.Trim() ?? "";
        var errors = new Dictionary<string, string[]>();
        if (displayName.Length is 0 or > UserProfile.MaxDisplayNameLength)
            errors["displayName"] = [$"Must be 1-{UserProfile.MaxDisplayNameLength} characters."];
        if (bio.Length > UserProfile.MaxBioLength)
            errors["bio"] = [$"Must be at most {UserProfile.MaxBioLength} characters."];
        if (errors.Count > 0) return Error.Validation("profile.invalid", "Profile is invalid.", errors);

        if (await db.UserProfiles.AnyAsync(p => p.Handle == handle.Value && p.Id != profile.Id, ct))
            return HandleTaken;

        profile.Update(handle.Value, displayName, bio);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            return HandleTaken;
        }

        return ProfileDto.From(profile, clock.UtcNow);
    }

    public static readonly Error HandleTaken = Error.Conflict("profile.handle_taken", "That handle is already taken.");
}
