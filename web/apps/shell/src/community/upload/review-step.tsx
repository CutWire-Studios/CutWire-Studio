import type { FieldViolation, ItemDetail, KindInfo } from "@cutwire/api-client"
import { Badge, Button } from "@cutwire/ui"
import { AlertCircle } from "lucide-react"
import { STEPS, groupByStep, violationMessage, type Step } from "../lib/violations"
import { FLAG_CODES } from "../lib/live-check"
import { formatBytes } from "../lib/text"
import type { Details } from "./details-step"
import { NoticeList } from "./shared"

export function ReviewStep({
  kind,
  details,
  item,
  localIssues,
  violations,
  onGo,
}: {
  kind: KindInfo
  details: Details
  item: ItemDetail
  localIssues: { step: Step; message: string }[]
  violations: FieldViolation[]
  onGo: (s: Step) => void
}) {
  const blocking = violations.filter((v) => !FLAG_CODES.has(v.code))
  const flags = violations.filter((v) => FLAG_CODES.has(v.code))
  const grouped = groupByStep(blocking)
  const current = item.versions[0]
  return (
    <div className="space-y-6">
      <dl className="panel grid gap-x-6 gap-y-2 p-4 text-sm sm:grid-cols-[8rem_1fr]">
        <dt className="text-muted-foreground">Kind</dt>
        <dd>{kind.displayName}</dd>
        <dt className="text-muted-foreground">Title</dt>
        <dd className="break-words">{details.title}</dd>
        <dt className="text-muted-foreground">Summary</dt>
        <dd className="break-words">{details.summary}</dd>
        <dt className="text-muted-foreground">Category</dt>
        <dd>{details.category}</dd>
        <dt className="text-muted-foreground">Tags</dt>
        <dd className="flex flex-wrap gap-1">{details.tags.map((t) => <Badge key={t}>{t}</Badge>)}</dd>
        <dt className="text-muted-foreground">License</dt>
        <dd>{details.license}</dd>
        <dt className="text-muted-foreground">Package</dt>
        <dd>{current ? `v${current.version}, ${formatBytes(Number(current.size))}` : "Missing"}</dd>
        <dt className="text-muted-foreground">Media</dt>
        <dd>{item.media.map((m) => m.role).join(", ") || "None"}</dd>
      </dl>

      {localIssues.length > 0 && (
        <div className="space-y-2" role="alert">
          <h3 className="text-label-md">Fix before publishing</h3>
          <ul className="space-y-1">
            {localIssues.map((i, n) => (
              <li key={n} className="flex items-center gap-2 text-sm text-destructive">
                <AlertCircle className="size-4 shrink-0" aria-hidden />
                <span className="flex-1">{i.message}</span>
                <Button variant="ghost" size="sm" onClick={() => onGo(i.step)}>
                  Go to {STEPS.find((s) => s.id === i.step)?.label}
                </Button>
              </li>
            ))}
          </ul>
        </div>
      )}

      {STEPS.map((s) =>
        grouped[s.id].length ? (
          <div key={s.id} className="space-y-2" role="alert">
            <div className="flex items-center justify-between">
              <h3 className="text-label-md">The server rejected: {s.label}</h3>
              {s.id !== "review" && (
                <Button variant="secondary" size="sm" onClick={() => onGo(s.id)}>
                  Fix in {s.label}
                </Button>
              )}
            </div>
            <ul className="space-y-1">
              {grouped[s.id].map((v, i) => (
                <li key={i} className="text-sm text-destructive" data-code={v.code}>
                  {violationMessage(v)}
                </li>
              ))}
            </ul>
          </div>
        ) : null,
      )}

      {flags.length > 0 && (
        <div className="space-y-2">
          <h3 className="text-label-md">Notices (these do not block publishing)</h3>
          <NoticeList violations={flags} />
        </div>
      )}
    </div>
  )
}
