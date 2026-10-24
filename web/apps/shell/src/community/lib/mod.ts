import type { ReportSummary } from "@cutwire/api-client"

/** Reports grouped by reason, most common first. */
export function summarizeReports(reports: Pick<ReportSummary, "reason">[]): { reason: string; count: number }[] {
  const counts = new Map<string, number>()
  for (const r of reports) counts.set(r.reason, (counts.get(r.reason) ?? 0) + 1)
  return [...counts].map(([reason, count]) => ({ reason, count })).sort((a, b) => b.count - a.count || a.reason.localeCompare(b.reason))
}
