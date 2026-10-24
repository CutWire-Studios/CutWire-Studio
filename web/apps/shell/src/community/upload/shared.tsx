import type { FieldViolation } from "@cutwire/api-client"
import { cn } from "@cutwire/ui"
import { AlertCircle, Check, Info } from "lucide-react"
import { STEPS, violationMessage, type Step } from "../lib/violations"

export function FieldErrors({ violations, id }: { violations?: Pick<FieldViolation, "code" | "message" | "params">[]; id?: string }) {
  if (!violations?.length) return null
  return (
    <ul id={id} className="space-y-1" role="alert">
      {violations.map((v, i) => (
        <li key={`${v.code}${i}`} className="flex items-start gap-1.5 text-xs text-destructive" data-code={v.code}>
          <AlertCircle className="mt-0.5 size-3.5 shrink-0" aria-hidden />
          {violationMessage(v)}
        </li>
      ))}
    </ul>
  )
}

export function NoticeList({ violations }: { violations: FieldViolation[] }) {
  if (!violations.length) return null
  return (
    <ul className="space-y-2">
      {violations.map((v, i) => (
        <li key={i} role="status" className="flex items-start gap-2 rounded-lg border border-border bg-muted px-3 py-2 text-sm">
          <Info className="mt-0.5 size-4 shrink-0" aria-hidden />
          {violationMessage(v)}
        </li>
      ))}
    </ul>
  )
}

export function Stepper({ step, reachable, issues, onGo }: { step: Step; reachable: Step[]; issues: Partial<Record<Step, number>>; onGo: (s: Step) => void }) {
  const at = STEPS.findIndex((s) => s.id === step)
  return (
    <ol className="flex flex-wrap gap-2" aria-label="Upload steps">
      {STEPS.map((s, i) => {
        const can = reachable.includes(s.id)
        const n = issues[s.id] ?? 0
        return (
          <li key={s.id}>
            <button
              type="button"
              disabled={!can}
              aria-current={s.id === step ? "step" : undefined}
              onClick={() => onGo(s.id)}
              className={cn(
                "text-label-md inline-flex min-h-9 cursor-pointer items-center gap-2 rounded-full border px-4 transition-colors disabled:cursor-not-allowed disabled:opacity-50",
                s.id === step ? "border-primary bg-primary text-primary-foreground" : "border-border hover:bg-muted",
                n > 0 && s.id !== step && "border-destructive text-destructive",
              )}
            >
              {i < at && n === 0 ? <Check className="size-4" aria-hidden /> : <span>{i + 1}</span>}
              {s.label}
              {n > 0 && <span className="rounded-full bg-destructive px-1.5 text-xs text-destructive-foreground">{n}</span>}
            </button>
          </li>
        )
      })}
    </ol>
  )
}
