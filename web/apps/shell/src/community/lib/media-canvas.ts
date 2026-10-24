import { coverRect } from "./media"

export interface LoadedImage {
  source: CanvasImageSource
  width: number
  height: number
  close: () => void
}

export async function loadImage(blob: Blob): Promise<LoadedImage> {
  const bmp = await createImageBitmap(blob)
  return { source: bmp, width: bmp.width, height: bmp.height, close: () => bmp.close() }
}

export interface RenderOptions {
  zoom: number
  focusX: number
  focusY: number
}

export function renderCover(img: LoadedImage, width: number, height: number, opts: RenderOptions): HTMLCanvasElement {
  const canvas = document.createElement("canvas")
  canvas.width = width
  canvas.height = height
  const ctx = canvas.getContext("2d")
  if (!ctx) throw new Error("Canvas is not available in this browser.")
  ctx.imageSmoothingQuality = "high"
  const r = coverRect(img.width, img.height, width, height, opts.zoom, opts.focusX, opts.focusY)
  ctx.drawImage(img.source, r.sx, r.sy, r.sw, r.sh, 0, 0, width, height)
  return canvas
}

export function encodeWebp(canvas: HTMLCanvasElement, quality: number): Promise<Blob> {
  return new Promise((resolve, reject) =>
    canvas.toBlob(
      (b) => (b && b.type === "image/webp" ? resolve(b) : reject(new Error("This browser cannot encode WebP."))),
      "image/webp",
      quality,
    ),
  )
}
