import type { JsonObject } from "@cutwire/drift-theme"
import { toCanvas } from "html-to-image"
import { flushSync } from "react-dom"
import { createRoot } from "react-dom/client"
import { DriftPreview, PREVIEW_H, PREVIEW_W } from "../preview/DriftPreview"
import { PreviewProvider } from "../preview/kit"

const frame = () => new Promise<void>((r) => requestAnimationFrame(() => r()))

/** Renders the mock window at exactly 1280x720 off-screen and rasterises it. */
export async function capturePreview(doc: JsonObject): Promise<HTMLCanvasElement> {
  const host = document.createElement("div")
  host.style.cssText = `position:fixed;left:-10000px;top:0;width:${PREVIEW_W}px;height:${PREVIEW_H}px;pointer-events:none`
  document.body.appendChild(host)
  const root = createRoot(host)
  try {
    flushSync(() =>
      root.render(
        <PreviewProvider value={{ doc, selected: null, select: () => {} }}>
          <DriftPreview dialog={false} capture />
        </PreviewProvider>,
      ),
    )
    await frame()
    await frame()
    const node = host.firstElementChild as HTMLElement
    const canvas = await toCanvas(node, { width: PREVIEW_W, height: PREVIEW_H, pixelRatio: 1, skipFonts: true, canvasWidth: PREVIEW_W, canvasHeight: PREVIEW_H })
    if (canvas.width === PREVIEW_W && canvas.height === PREVIEW_H) return canvas
    const exact = document.createElement("canvas")
    exact.width = PREVIEW_W
    exact.height = PREVIEW_H
    exact.getContext("2d")?.drawImage(canvas, 0, 0, PREVIEW_W, PREVIEW_H)
    return exact
  } finally {
    root.unmount()
    host.remove()
  }
}

export function encodeWebp(canvas: HTMLCanvasElement, quality: number): Promise<Blob> {
  return new Promise((resolve, reject) =>
    canvas.toBlob(
      (b) => {
        if (!b) reject(new Error("Could not encode the screenshot."))
        else if (b.type !== "image/webp") reject(new Error("This browser cannot encode WebP. Use a Chromium or Firefox based browser to publish."))
        else resolve(b)
      },
      "image/webp",
      quality,
    ),
  )
}

/** Highest quality (to two decimals) that still fits under `cap` bytes, or null if even the lowest does not. */
export async function fitQuality(canvas: HTMLCanvasElement, cap: number): Promise<{ quality: number; blob: Blob } | null> {
  let lo = 0.05
  let hi = 0.95
  let best: { quality: number; blob: Blob } | null = null
  const floor = await encodeWebp(canvas, lo)
  if (floor.size > cap) return null
  best = { quality: lo, blob: floor }
  for (let i = 0; i < 7; i++) {
    const mid = Math.round(((lo + hi) / 2) * 100) / 100
    const blob = await encodeWebp(canvas, mid)
    if (blob.size <= cap) {
      best = { quality: mid, blob }
      lo = mid
    } else hi = mid
  }
  return best
}
