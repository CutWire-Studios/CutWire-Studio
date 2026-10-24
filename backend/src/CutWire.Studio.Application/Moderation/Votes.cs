using CutWire.Studio.Application.Abstractions;
using CutWire.Studio.Domain.Catalog;
using CutWire.Studio.Domain.Common;
using CutWire.Studio.Domain.Moderation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CutWire.Studio.Application.Moderation;

/// <param name="Value">1, -1, or null to clear the vote.</param>
public sealed record CastVoteCommand(Guid ItemId, Guid UserId, int? Value);

public sealed record VoteResultDto(int? ViewerVote, double ScoreUp, double ScoreDown, double Score);

public sealed class CastVoteHandler(
    IAppDbContext db, IClock clock, IUserDirectory directory, AutoHideService autoHide, IOptions<ModerationOptions> options)
    : ICommandHandler<CastVoteCommand, VoteResultDto>
{
    public async Task<Result<VoteResultDto>> Handle(CastVoteCommand c, CancellationToken ct)
    {
        if (c.Value is not (null or 1 or -1))
            return Error.Validation("bad_request", "Vote value must be 1 or -1.",
                [new FieldViolation("value", "invalid_value", "Vote value must be 1 or -1.")]);

        var meta = await db.Items.AsNoTracking().Where(i => i.Id == c.ItemId).Select(i => new { i.OwnerId, i.Status }).FirstOrDefaultAsync(ct);
        if (meta is null || meta.Status != ItemStatus.Published) return Error.NotFound("item.not_found", "Item not found.");
        if (meta.OwnerId == c.UserId) return Error.Forbidden("vote.own_item", "You cannot vote on your own item.");

        var weight = 0.0;
        if (c.Value is not null)
        {
            var profile = await db.UserProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.Id == c.UserId, ct);
            if (profile is null) return Error.NotFound("profile.not_found", "Profile not found.");
            var account = (await directory.GetAsync([c.UserId], ct)).GetValueOrDefault(c.UserId);
            if (account is not { EmailVerified: true })
                return Error.Forbidden("vote.email_unverified", "Verify your email address to vote.");
            weight = Vote.WeightFor(profile.TrustLevel, profile.CreatedAt, clock.UtcNow, options.Value.NewAccountVoteAge, options.Value.NewAccountVoteWeight);
        }

        await using var tx = await db.BeginTransactionAsync(ct);
        if (!await ItemLock.AcquireAsync(db, c.ItemId, ct)) return Error.NotFound("item.not_found", "Item not found.");
        if (await db.Items.AsNoTracking().Where(i => i.Id == c.ItemId).Select(i => i.Status).FirstAsync(ct) != ItemStatus.Published)
            return Error.NotFound("item.not_found", "Item not found.");

        var now = clock.UtcNow;
        double up = 0, down = 0;
        var existing = await db.Votes.FirstOrDefaultAsync(v => v.ItemId == c.ItemId && v.UserId == c.UserId, ct);
        if (existing is not null)
        {
            if (existing.Value > 0) up -= existing.Weight; else down -= existing.Weight;
        }

        if (c.Value is { } value)
        {
            if (value > 0) up += weight; else down += weight;
            if (existing is null) db.Votes.Add(new Vote(c.ItemId, c.UserId, value, weight, now));
            else existing.Change(value, weight, now);
        }
        else if (existing is not null) db.Votes.Remove(existing);

        await db.SaveChangesAsync(ct);
        await db.Items.Where(i => i.Id == c.ItemId).ExecuteUpdateAsync(s => s
            .SetProperty(i => i.ScoreUp, i => Math.Max(i.ScoreUp + up, 0))
            .SetProperty(i => i.ScoreDown, i => Math.Max(i.ScoreDown + down, 0)), ct);

        var tally = await db.Items.AsNoTracking().Where(i => i.Id == c.ItemId)
            .Select(i => new { i.ScoreUp, i.ScoreDown, i.PublishedAt }).FirstAsync(ct);
        var wilson = Ranking.Wilson(tally.ScoreUp, tally.ScoreDown);
        var hot = tally.PublishedAt is { } published ? Ranking.Hot(wilson, published, now) : 0;
        await db.Items.Where(i => i.Id == c.ItemId).ExecuteUpdateAsync(s => s
            .SetProperty(i => i.WilsonScore, wilson).SetProperty(i => i.HotScore, hot), ct);

        await autoHide.EvaluateAsync(c.ItemId, ct);
        await tx.CommitAsync(ct);
        return new VoteResultDto(c.Value, tally.ScoreUp, tally.ScoreDown, tally.ScoreUp - tally.ScoreDown);
    }
}
