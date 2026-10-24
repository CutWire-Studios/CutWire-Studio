import { Button, Dialog, DialogContent, Spinner } from "@cutwire/ui"
import { useEffect, useRef, useState } from "react"
import { findQuality, type MediaSpec } from "../lib/media"
import { encodeWebp, loadImage, renderCover, type LoadedImage } from "../lib/media-canvas"
import { formatBytes } from "../lib/text"

const Slider = ({ label, value, min, max, step, onChange, display }: { label: string; value: number; min: number; max: number; step: number; onChange: (v: number) => void; display: string }) => (
  <label className="block space-y-1">
    <span className="text-label-md flex justify-between">
      {label}
      <span className="font-normal text-muted-foreground tabular-nums">{display}</span>
    </span>
    <input type="range" className="w-full accent-[var(--primary)]" min={min} max={max} step={step} value={value} onChange={(e) => onChange(Number(e.target.value))} />
  </label>
)

/** Crop/scale an arbitrary image to the spec size and re-encode as WebP until it fits the byte cap. */
export function MediaFixer({ source, spec, onAccept, onClose }: { source: Blob; spec: MediaSpec; onAccept: (b: Blob) => void; onClose: () => void }) {
  const [img, setImg] = useState<LoadedImage | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [zoom, setZoom] = useState(1)
  const [fx, setFx] = useState(0.5)
  const [fy, setFy] = useState(0.5)
  const [quality, setQuality] = useState(0.8)
  const [result, setResult] = useState<{ blob: Blob; url: string } | null>(null)
  const [busy, setBusy] = useState(false)
  const urlRef = useRef<string | null>(null)

  useEffect(() => {
    let live = true
    let loaded: LoadedImage | null = null
    loadImage(source).then(
      (i) => {
        loaded = i
        if (live) setImg(i)
        else i.close()
      },
      () => live && setError("This file is not an image the browser can read."),
    )
    return () => {
      live = false
      loaded?.close()
    }
  }, [source])

  useEffect(() => {
    if (!img) return
    let live = true
    setBusy(true)
    const t = setTimeout(async () => {
      try {
        const blob = await encodeWebp(renderCover(img, spec.width, spec.height, { zoom, focusX: fx, focusY: fy }), quality)
        if (!live) return
        const url = URL.createObjectURL(blob)
        if (urlRef.current) URL.revokeObjectURL(urlRef.current)
        urlRef.current = url
        setResult({ blob, url })
        setError(null)
      } catch (e) {
        if (live) setError(e instanceof Error ? e.message : "Could not encode the image.")
      } finally {
        if (live) setBusy(false)
      }
    }, 120)
    return () => {
      live = false
      clearTimeout(t)
    }
  }, [img, spec.width, spec.height, zoom, fx, fy, quality])

  useEffect(
    () => () => {
      if (urlRef.current) URL.revokeObjectURL(urlRef.current)
    },
    [],
  )

  const autoFit = async () => {
    if (!img) return
    setBusy(true)
    try {
      const canvas = renderCover(img, spec.width, spec.height, { zoom, focusX: fx, focusY: fy })
      const found = await findQuality(async (q) => (await encodeWebp(canvas, q)).size, spec.maxBytes)
      setQuality(Math.round(found.quality * 100) / 100)
    } catch (e) {
      setError(e instanceof Error ? e.message : "Could not encode the image.")
    } finally {
      setBusy(false)
    }
  }

  const bytes = result?.blob.size ?? 0
  const fits = !!result && bytes <= spec.maxBytes

  return (
    <Dialog open onOpenChange={(o) => !o && onClose()}>
      <DialogContent
        title={`Fix ${spec.role} image`}
        description={`Crops and scales to exactly ${spec.width}x${spec.height} px and saves as WebP under ${formatBytes(spec.maxBytes)}.`}
        className="max-w-2xl"
      >
        {error && !img ? (
          <p role="alert" className="text-sm text-destructive">{error}</p>
        ) : !img ? (
          <Spinner />
        ) : (
          <div className="space-y-4">
            <div className="flex min-h-24 items-center justify-center overflow-hidden rounded-lg bg-muted">
              {result && <img src={result.url} alt="Preview of the fixed image" width={spec.width} height={spec.height} className="max-h-72 w-auto max-w-full" />}
            </div>
            <div className="grid gap-3 sm:grid-cols-3">
              <Slider label="Zoom" value={zoom} min={1} max={4} step={0.05} onChange={setZoom} display={`${zoom.toFixed(2)}x`} />
              <Slider label="Horizontal" value={fx} min={0} max={1} step={0.01} onChange={setFx} display={`${Math.round(fx * 100)}%`} />
              <Slider label="Vertical" value={fy} min={0} max={1} step={0.01} onChange={setFy} display={`${Math.round(fy * 100)}%`} />
            </div>
            <Slider label="Quality" value={quality} min={0.05} max={0.95} step={0.01} onChange={setQuality} display={`${Math.round(quality * 100)}%`} />
            <div className="flex flex-wrap items-center justify-between gap-2">
              <p className={fits ? "text-sm text-muted-foreground" : "text-sm text-destructive"} aria-live="polite">
                {busy ? "Encoding..." : `${formatBytes(bytes)} / ${formatBytes(spec.maxBytes)}${fits ? "" : " (too large)"}`}
              </p>
              <Button variant="secondary" size="sm" onClick={() => void autoFit()} disabled={busy}>
                Fit under the limit
              </Button>
            </div>
            {error && <p role="alert" className="text-sm text-destructive">{error}</p>}
            <div className="flex justify-end gap-2">
              <Button variant="secondary" onClick={onClose}>
                Cancel
              </Button>
              <Button disabled={!fits || busy} onClick={() => result && onAccept(result.blob)}>
                Use this image
              </Button>
            </div>
          </div>
        )}
      </DialogContent>
    </Dialog>
  )
}
