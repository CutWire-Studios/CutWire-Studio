import { findToken } from "@cutwire/drift-theme"
import { Switch } from "@cutwire/ui"
import { useCallback, useEffect, useRef, useState } from "react"
import { useComputed } from "@/state/computed"
import { useUi } from "@/state/ui"
import { DriftPreview, PREVIEW_H, PREVIEW_W } from "./DriftPreview"
import { PreviewProvider } from "./kit"

export function PreviewStage() {
  const { resolved } = useComputed()
  const selected = useUi((s) => s.selected)
  const dialogOpen = useUi((s) => s.dialogOpen)
  const setDialogOpen = useUi((s) => s.setDialogOpen)
  const select = useUi((s) => s.select)
  const box = useRef<HTMLDivElement>(null)
  const [scale, setScale] = useState(0.7)

  useEffect(() => {
    const el = box.current
    if (!el) return
    const fit = () => setScale(Math.max(0.2, Math.min((el.clientWidth - 24) / PREVIEW_W, (el.clientHeight - 24) / PREVIEW_H)))
    fit()
    const ro = new ResizeObserver(fit)
    ro.observe(el)
    return () => ro.disconnect()
  }, [])

  const onSelect = useCallback((p: string) => select(p, findToken(p)?.group), [select])

  return (
    <div className="flex h-full min-h-0 flex-col">
      <div className="flex items-center gap-4 border-b border-border px-3 py-2 text-xs text-muted-foreground">
        <span className="font-medium text-foreground">Live preview</span>
        <span>
          {resolved.name} · {resolved.variant} · {Math.round(scale * 100)}%
        </span>
        <label className="ml-auto flex items-center gap-2">
          Dialog
          <Switch checked={dialogOpen} onCheckedChange={setDialogOpen} aria-label="Show dialog" />
        </label>
      </div>
      <div ref={box} className="flex min-h-0 flex-1 items-center justify-center overflow-hidden bg-muted/40 p-3" onClick={() => select(null)}>
        <div style={{ width: PREVIEW_W * scale, height: PREVIEW_H * scale }} className="shrink-0 overflow-hidden rounded-lg shadow-xl ring-1 ring-border">
          <div style={{ width: PREVIEW_W, height: PREVIEW_H, transform: `scale(${scale})`, transformOrigin: "top left" }}>
            <PreviewProvider value={{ doc: resolved.document, selected, select: onSelect }}>
              <DriftPreview dialog={dialogOpen} />
            </PreviewProvider>
          </div>
        </div>
      </div>
    </div>
  )
}
