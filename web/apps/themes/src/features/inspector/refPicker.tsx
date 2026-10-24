import { getPath, isPair, toCss, type Json, type JsonObject, type TokenEntry } from "@cutwire/drift-theme"
import { Input } from "@cutwire/ui"
import { useMemo, useState } from "react"
import { compatibleTargets, matchesQuery, wouldCycle } from "@/lib/tokens"
import { Swatch } from "./color"

export function display(v: Json | undefined): string {
  if (v === undefined) return "-"
  if (isPair(v)) return `${String(v.pointer)} / ${String(v.touch)}`
  return typeof v === "string" ? v : JSON.stringify(v)
}

export function ValuePreview({ token, value }: { token: Pick<TokenEntry, "type" | "of">; value: Json | undefined }) {
  if (token.type === "color" && typeof value === "string") return <Swatch css={toCss(value)} className="size-5" />
  return <span className="max-w-24 truncate font-mono text-[11px] text-muted-foreground">{display(value)}</span>
}

interface Props {
  /** The token being edited; cycles back to it are refused. */
  self: string
  kind: Pick<TokenEntry, "type" | "of" | "path" | "category" | "group">
  merged: JsonObject
  resolved: JsonObject
  current: string | null
  onPick(ref: string): void
}

/** Lists tokens a `$reference` may point at. Choices that would loop back are disabled. */
export function RefPicker({ self, kind, merged, resolved, current, onPick }: Props) {
  const [q, setQ] = useState("")
  const targets = useMemo(() => compatibleTargets(kind as TokenEntry), [kind])
  const shown = useMemo(() => targets.filter((t) => !q || matchesQuery(t, q)).slice(0, 150), [targets, q])
  return (
    <div className="flex flex-col gap-2 rounded-lg border border-border bg-surface p-2">
      <Input aria-label="Search tokens" placeholder="Search palette, semantic and metrics tokens" value={q} onChange={(e) => setQ(e.target.value)} className="min-h-8 py-1" />
      <ul className="max-h-56 overflow-y-auto">
        {shown.map((t) => {
          const cyc = wouldCycle(merged, self, t.path)
          return (
            <li key={t.path}>
              <button
                type="button"
                disabled={cyc}
                title={cyc ? "Would create a reference cycle" : undefined}
                onClick={() => onPick(`$${t.path}`)}
                className={`flex w-full cursor-pointer items-center gap-2 rounded-md px-2 py-1 text-left text-xs hover:bg-muted disabled:cursor-not-allowed disabled:opacity-40 ${current === `$${t.path}` ? "bg-muted font-semibold" : ""}`}
              >
                <ValuePreview token={t} value={getPath(resolved, t.path)} />
                <span className="min-w-0 flex-1 truncate font-mono">{t.path}</span>
                {cyc && <span className="text-[10px] text-destructive">cycle</span>}
              </button>
            </li>
          )
        })}
        {shown.length === 0 && <li className="px-2 py-3 text-xs text-muted-foreground">No compatible tokens.</li>}
      </ul>
      {targets.length > shown.length && !q && <p className="text-[11px] text-muted-foreground">Showing the first {shown.length} of {targets.length}. Search to narrow down.</p>}
    </div>
  )
}
