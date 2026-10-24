import { CheckCircle2 } from "lucide-react"
import { findToken } from "@cutwire/drift-theme"
import { useComputed } from "@/state/computed"
import { useUi } from "@/state/ui"

export function IssuesPanel() {
  const { validation, error } = useComputed()
  const select = useUi((s) => s.select)
  const all = [
    ...(error ? [{ path: "extends", message: error, code: "error", level: "error" }] : []),
    ...validation.errors.map((e) => ({ ...e, level: "error" })),
    ...validation.warnings.map((e) => ({ ...e, level: "warning" })),
  ]
  if (all.length === 0) {
    return (
      <p className="flex items-center gap-2 p-4 text-sm">
        <CheckCircle2 className="size-4 text-primary" aria-hidden /> Valid against the Drift schema.
      </p>
    )
  }
  return (
    <ul className="flex flex-col p-2">
      {all.slice(0, 200).map((i, n) => {
        const token = i.path ? findToken(i.path) : undefined
        return (
          <li key={n}>
            <button type="button" disabled={!token} onClick={() => token && select(token.path, token.group)} className="flex w-full cursor-pointer flex-col gap-0.5 rounded-md px-2 py-1.5 text-left hover:bg-muted disabled:cursor-default">
              <span className={`text-xs font-semibold ${i.level === "error" ? "text-destructive" : "text-muted-foreground"}`}>{i.level}</span>
              <span className="text-xs">{i.message}</span>
              {i.path && <span className="font-mono text-[11px] text-muted-foreground">{i.path}</span>}
            </button>
          </li>
        )
      })}
    </ul>
  )
}
