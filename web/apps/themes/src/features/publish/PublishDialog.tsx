import { Button, Dialog, DialogContent, Spinner } from "@cutwire/ui"
import { AlertTriangle, Upload } from "lucide-react"
import { useEffect, useRef, useState } from "react"
import { buildHandoff, SCREENSHOT_CAP_BYTES, UPLOAD_URL, writeHandoff } from "@/services/publish"
import { useComputed } from "@/state/computed"
import { useEditor } from "@/state/editor"
import { capturePreview, encodeWebp, fitQuality } from "./capture"

type Phase = "capturing" | "ready" | "failed"

function usePublishCapture(open: boolean) {
  const { resolved } = useComputed()
  const canvas = useRef<HTMLCanvasElement | null>(null)
  const [phase, setPhase] = useState<Phase>("capturing")
  const [error, setError] = useState("")
  const [quality, setQuality] = useState(0.8)
  const [shot, setShot] = useState<{ blob: Blob; url: string } | null>(null)

  useEffect(() => {
    if (!open) return
    let dead = false
    setPhase("capturing")
    capturePreview(resolved.document)
      .then(async (c) => {
        canvas.current = c
        const fit = await fitQuality(c, SCREENSHOT_CAP_BYTES)
        if (dead) return
        setQuality(fit?.quality ?? 0.05)
        setPhase("ready")
      })
      .catch((e: unknown) => {
        if (dead) return
        setError(e instanceof Error ? e.message : String(e))
        setPhase("failed")
      })
    return () => {
      dead = true
    }
    // The screenshot is taken once per time the dialog opens.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open])

  useEffect(() => {
    if (phase !== "ready" || !canvas.current) return
    let dead = false
    const timer = setTimeout(() => {
      encodeWebp(canvas.current as HTMLCanvasElement, quality)
        .then((blob) => {
          if (dead) return
          setShot((prev) => {
            if (prev) URL.revokeObjectURL(prev.url)
            return { blob, url: URL.createObjectURL(blob) }
          })
        })
        .catch((e: unknown) => {
          if (dead) return
          setError(e instanceof Error ? e.message : String(e))
          setPhase("failed")
        })
    }, 120)
    return () => {
      dead = true
      clearTimeout(timer)
    }
  }, [phase, quality])

  const autoFit = async () => {
    if (!canvas.current) return
    const fit = await fitQuality(canvas.current, SCREENSHOT_CAP_BYTES)
    if (fit) setQuality(fit.quality)
  }
  return { phase, error, quality, setQuality, shot, autoFit }
}

export function PublishDialog({ open, onOpenChange }: { open: boolean; onOpenChange(o: boolean): void }) {
  const { validation } = useComputed()
  const doc = useEditor((s) => s.doc)
  const cap = SCREENSHOT_CAP_BYTES
  const { phase, error, quality, setQuality, shot, autoFit } = usePublishCapture(open)
  const [busy, setBusy] = useState(false)
  const [failure, setFailure] = useState("")
  const bytes = shot?.blob.size ?? 0
  const over = bytes > cap
  const canContinue = phase === "ready" && !!shot && !over && validation.valid && !busy

  const send = async () => {
    if (!shot) return
    setBusy(true)
    try {
      await writeHandoff(buildHandoff(doc, shot.blob))
      location.assign(UPLOAD_URL)
    } catch (e) {
      setFailure(e instanceof Error ? e.message : String(e))
      setBusy(false)
    }
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent title="Publish to Community" description="Your theme is sent to the upload page with a 1280 by 720 screenshot of the preview." className="max-w-2xl">
        <div className="flex flex-col gap-4">
          <div className="relative aspect-video overflow-hidden rounded-lg border border-border bg-muted">
            {shot ? <img src={shot.url} alt="Theme screenshot" className="size-full object-cover" /> : <div className="flex size-full items-center justify-center"><Spinner /></div>}
          </div>
          {phase === "failed" && <p className="text-sm text-destructive">{error}</p>}
          <div className="flex flex-col gap-2">
            <label className="flex items-center gap-3 text-sm">
              <span className="w-16 text-muted-foreground">Quality</span>
              <input type="range" min={0.05} max={0.95} step={0.01} value={quality} disabled={phase !== "ready"} onChange={(e) => setQuality(+e.target.value)} className="flex-1 accent-primary" />
              <span className="w-10 text-right font-mono text-xs">{Math.round(quality * 100)}</span>
              <Button variant="secondary" size="sm" disabled={phase !== "ready"} onClick={() => void autoFit()}>
                Fit to cap
              </Button>
            </label>
            <div>
              <div className="flex justify-between font-mono text-xs">
                <span className={over ? "text-destructive" : ""}>{(bytes / 1000).toFixed(1)} KB</span>
                <span className="text-muted-foreground">cap {(cap / 1000).toFixed(0)} KB</span>
              </div>
              <div className="mt-1 h-2 overflow-hidden rounded-full bg-muted">
                <div className={`h-full ${over ? "bg-destructive" : "bg-primary"}`} style={{ width: `${Math.min(100, (bytes / cap) * 100)}%` }} />
              </div>
              {over && <p className="mt-1 text-xs text-destructive">Over the cap. Lower the quality.</p>}
            </div>
          </div>
          {!validation.valid && (
            <p className="flex items-start gap-2 text-sm text-destructive">
              <AlertTriangle className="mt-0.5 size-4 shrink-0" aria-hidden />
              Fix the {validation.errors.length} problem{validation.errors.length > 1 ? "s" : ""} in the Issues tab before publishing.
            </p>
          )}
          {failure && <p className="text-sm text-destructive">{failure}</p>}
          <div className="flex justify-end gap-2">
            <Button variant="secondary" onClick={() => onOpenChange(false)}>
              Cancel
            </Button>
            <Button disabled={!canContinue} onClick={() => void send()}>
              <Upload className="size-4" aria-hidden /> Continue to upload
            </Button>
          </div>
        </div>
      </DialogContent>
    </Dialog>
  )
}
