using CutWire.Studio.Application.Abstractions;
using CutWire.Studio.Domain.Catalog;
using CutWire.Studio.Domain.Common;
using CutWire.Studio.Domain.Moderation;
using Microsoft.EntityFrameworkCore;

namespace CutWire.Studio.Application.Moderation;

public sealed record CreateReportCommand(Guid ItemId, Guid ReporterId, string? Reason, string? Note);

public sealed record ReportDto(Guid Id, Guid ItemId, string Reason, string Status, DateTimeOffset CreatedAt);

public sealed class CreateReportHandler(IAppDbContext db, IClock clock, AutoHideService autoHide)
    : ICommandHandler<CreateReportCommand, ReportDto>
{
    public async Task<Result<ReportDto>> Handle(CreateReportCommand c, CancellationToken ct)
    {
        if (!ReportReasonExtensions.TryParse(c.Reason, out var reason))
            return Error.Validation("bad_request", "Unknown report reason.", [new FieldViolation("reason", "invalid_reason",
                "Reason must be one of spam, low_quality, broken, offensive, copyright, malware, other.")]);
        var note = string.IsNullOrWhiteSpace(c.Note) ? null : c.Note.Trim();
        if (note is { Length: > Report.MaxNoteLength })
            return Error.Validation("bad_request", "The note is too long.", [new FieldViolation("note", "too_long",
                $"Note must be at most {Report.MaxNoteLength} characters.", new Dictionary<string, object> { ["max"] = Report.MaxNoteLength })]);

        await using var tx = await db.BeginTransactionAsync(ct);
        if (!await ItemLock.AcquireAsync(db, c.ItemId, ct)) return Error.NotFound("item.not_found", "Item not found.");
        var item = await db.Items.AsNoTracking().Where(i => i.Id == c.ItemId).Select(i => new { i.OwnerId, i.Status }).FirstAsync(ct);
        if (item.Status != ItemStatus.Published) return Error.NotFound("item.not_found", "Item not found.");
        if (item.OwnerId == c.ReporterId) return Error.Forbidden("report.own_item", "You cannot report your own item.");
        if (await db.Reports.AnyAsync(r => r.ItemId == c.ItemId && r.ReporterId == c.ReporterId && r.Status == ReportStatus.Open, ct))
            return Error.Conflict("report.already_reported", "You already have an open report on this item.");

        var report = new Report(Guid.CreateVersion7(), c.ItemId, c.ReporterId, reason, note, clock.UtcNow);
        db.Reports.Add(report);
        await db.SaveChangesAsync(ct);
        await autoHide.EvaluateAsync(c.ItemId, ct);
        await tx.CommitAsync(ct);
        return new ReportDto(report.Id, report.ItemId, reason.ToKey(), "open", report.CreatedAt);
    }
}
