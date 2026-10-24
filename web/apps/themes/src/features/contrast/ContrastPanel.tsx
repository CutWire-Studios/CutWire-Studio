import { toCss } from "@cutwire/drift-theme"
import { Badge } from "@cutwire/ui"
import { AlertTriangle, CheckCircle2 } from "lucide-react"
import { useMemo, useState } from "react"
import { useComputed } from "@/state/computed"
import { useUi } from "@/state/ui"
import { contrastRows, rowLevel, THRESHOLD, THRESHOLD_LARGE } from "./pairs"

export function ContrastPanel() {
  const { resolved } = useComputed()
  const select = useUi((s) => s.select)
  const [large, setLarge] = useState<Record<string, boolean>>({})
  const rows = useMemo(() => contrastRows(resolved.document, resolved.variant), [resolved.document, resolved.variant])
  const failing = rows.filter((r) => rowLevel(r, !!large[r.id]) === "fail").length
  return (
    <div className="flex flex-col gap-3 p-3">
      <p className="flex items-center gap-2 text-sm">
        {failing > 0 ? <AlertTriangle className="size-4 text-destructive" aria-hidden /> : <CheckCircle2 className="size-4 text-primary" aria-hidden />}
        {failing > 0 ? `${failing} pair${failing > 1 ? "s" : ""} below ${THRESHOLD}:1` : "All pairs pass"}
      </p>
      <ul className="flex flex-col gap-1">
        {rows.map((r) => {
          const isLarge = !!large[r.id]
          const level = rowLevel(r, isLarge)
          return (
            <li key={r.id} className="flex items-center gap-2 rounded-md px-1 py-1 hover:bg-muted">
              <button type="button" className="flex min-w-0 flex-1 cursor-pointer items-center gap-2 text-left" onClick={() => select(r.fg)} title={`${r.fg} on ${r.bg}`}>
                <span className="inline-flex size-8 shrink-0 items-center justify-center rounded-md border border-border text-sm font-bold" style={{ background: r.bgValue ? toCss(r.bgValue) : undefined, color: r.fgValue ? toCss(r.fgValue) : undefined }}>
                  Aa
                </span>
                <span className="min-w-0 flex-1 truncate text-xs">{r.label}</span>
              </button>
              <label className="flex items-center gap-1 text-[10px] text-muted-foreground" title={`Large text only needs ${THRESHOLD_LARGE}:1`}>
                <input type="checkbox" checked={isLarge} onChange={(e) => setLarge({ ...large, [r.id]: e.target.checked })} />
                large
              </label>
              <span className="w-12 text-right font-mono text-xs">{r.ratio === null ? "n/a" : `${r.ratio.toFixed(2)}`}</span>
              {level && (
                <Badge tone={level === "fail" ? "neutral" : "primary"} className={level === "fail" ? "w-12 justify-center bg-destructive text-destructive-foreground" : "w-12 justify-center"}>
                  {level === "fail" ? "Fail" : level.toUpperCase()}
                </Badge>
              )}
            </li>
          )
        })}
      </ul>
      <p className="text-[11px] text-muted-foreground">Translucent colours are flattened over black (dark) or white (light) before measuring.</p>
    </div>
  )
}
