using static CutWire.Studio.Application.Quality.TextRules;

namespace CutWire.Studio.Application.Quality;

public sealed class UploadQuotaGate(ContentRulesOptions rules, IUploadQuota quota) : IContentGate
{
    public async Task<IReadOnlyList<GateViolation>> CheckAsync(SubmissionDraft draft, CancellationToken ct)
    {
        var usage = await quota.GetUsageAsync(draft.OwnerId, ct);
        var limits = rules.DailyUploadLimits;
        var limit = usage.TrustLevel >= 0 && usage.TrustLevel < limits.Length ? limits[usage.TrustLevel] : 0;
        if (limit > 0 && usage.UploadsInLastDay >= limit)
            return [V("account", "upload_limit", $"Daily upload limit of {limit} reached.", ("limit", limit), ("trustLevel", usage.TrustLevel))];
        return [];
    }
}
