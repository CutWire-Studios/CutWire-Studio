using CutWire.Studio.Application.Abstractions;
using CutWire.Studio.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace CutWire.Studio.Application.Accounts;

public sealed record GetProfileQuery(Guid UserId);

public sealed class GetProfileHandler(IAppDbContext db, IClock clock) : IQueryHandler<GetProfileQuery, ProfileDto>
{
    public async Task<Result<ProfileDto>> Handle(GetProfileQuery query, CancellationToken ct)
    {
        var profile = await db.UserProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.Id == query.UserId, ct);
        return profile is null
            ? Error.NotFound("profile.not_found", "Profile not found.")
            : ProfileDto.From(profile, clock.UtcNow);
    }
}
