import type { ForgeDoc } from "@cutwire/forge-core/doc/types"
import { allItems, rackOf } from "@cutwire/forge-core/audio/rack"
import { getEngine } from "@/services/preview/engine"
import { KIND_MEDIA } from "./webp"

export interface CommunityFrames {
  thumbnail: ImageData
  /** transitions: 12 frames across progress, 1536x128 */
  strip?: ImageData
}

/** Renders the community thumbnail (and transition strip) offscreen at their exact sizes. */
export function captureFrames(doc: ForgeDoc): CommunityFrames | string {
  if (doc.kind === "audio") return { thumbnail: audioThumbnail(doc) }
  return getEngine().renderCommunityFrames(doc, KIND_MEDIA.thumbnail.width, KIND_MEDIA.thumbnail.height)
}

/** Audio effects have nothing to render with WebGL: a card with the name and the pedal chain. */
function audioThumbnail(doc: ForgeDoc): ImageData {
  const { width: w, height: h } = KIND_MEDIA.thumbnail
  const c = document.createElement("canvas")
  c.width = w
  c.height = h
  const g = c.getContext("2d")!
  const bg = g.createLinearGradient(0, 0, w, h)
  bg.addColorStop(0, "#1e1b4b")
  bg.addColorStop(1, "#0f172a")
  g.fillStyle = bg
  g.fillRect(0, 0, w, h)
  g.strokeStyle = "rgba(255,255,255,0.35)"
  g.lineWidth = 3
  g.beginPath()
  for (let x = 0; x <= w; x += 4) {
    const y = h * 0.3 + Math.sin(x / 22) * 34 * Math.sin(x / 140 + 0.6)
    if (x === 0) g.moveTo(x, y)
    else g.lineTo(x, y)
  }
  g.stroke()
  g.fillStyle = "#fff"
  g.font = "600 40px sans-serif"
  g.fillText(doc.meta.displayName.slice(0, 28), 28, h * 0.62, w - 56)
  g.fillStyle = "rgba(255,255,255,0.75)"
  g.font = "22px sans-serif"
  const chain = allItems(rackOf(doc))
    .map((i) => i.type)
    .slice(0, 6)
    .join("  >  ")
  g.fillText(chain || "Audio effect", 28, h * 0.82, w - 56)
  return g.getImageData(0, 0, w, h)
}

export function encodeWebp(img: ImageData, quality: number): Promise<Blob> {
  const c = document.createElement("canvas")
  c.width = img.width
  c.height = img.height
  c.getContext("2d")!.putImageData(img, 0, 0)
  return new Promise((resolve, reject) =>
    c.toBlob(
      (b) => (b && b.type === "image/webp" ? resolve(b) : reject(new Error("This browser can't encode WebP images."))),
      "image/webp",
      quality,
    ),
  )
}
