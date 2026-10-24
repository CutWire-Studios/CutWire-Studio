export interface MediaSpec {
  role: string
  required: boolean
  width: number
  height: number
  maxBytes: number
  mimeType: string
}

export type MediaProblem =
  | { code: "not_webp" }
  | { code: "wrong_dimensions"; width: number; height: number; expectedWidth: number; expectedHeight: number }
  | { code: "too_large"; bytes: number; maxBytes: number }
  | { code: "undecodable" }

export interface MediaFacts {
  isWebp: boolean
  width: number | null
  height: number | null
  bytes: number
}

/** RIFF....WEBP magic. The declared mime is ignored, as on the server. */
export function isWebp(head: Uint8Array): boolean {
  if (head.length < 12) return false
  const tag = (o: number, s: string) => [...s].every((c, i) => head[o + i] === c.charCodeAt(0))
  return tag(0, "RIFF") && tag(8, "WEBP")
}

/** Reads the canvas size from a WebP header (VP8, VP8L or VP8X). Returns null when it cannot be determined. */
export function readWebpSize(b: Uint8Array): { width: number; height: number } | null {
  if (!isWebp(b) || b.length < 30) return null
  const fourcc = String.fromCharCode(b[12]!, b[13]!, b[14]!, b[15]!)
  if (fourcc === "VP8X") {
    const width = 1 + (b[24]! | (b[25]! << 8) | (b[26]! << 16))
    const height = 1 + (b[27]! | (b[28]! << 8) | (b[29]! << 16))
    return { width, height }
  }
  if (fourcc === "VP8L") {
    if (b[20] !== 0x2f) return null
    const bits = (b[21]! | (b[22]! << 8) | (b[23]! << 16) | (b[24]! << 24)) >>> 0
    return { width: (bits & 0x3fff) + 1, height: ((bits >>> 14) & 0x3fff) + 1 }
  }
  if (fourcc === "VP8 ") {
    if (b[23] !== 0x9d || b[24] !== 0x01 || b[25] !== 0x2a) return null
    return { width: (b[26]! | (b[27]! << 8)) & 0x3fff, height: (b[28]! | (b[29]! << 8)) & 0x3fff }
  }
  return null
}

/** Pre-upload check against a MediaSpec. Returns every problem, in the order the server reports them. */
export function checkMedia(facts: MediaFacts, spec: Pick<MediaSpec, "width" | "height" | "maxBytes">): MediaProblem[] {
  const problems: MediaProblem[] = []
  if (!facts.isWebp) problems.push({ code: "not_webp" })
  if (facts.bytes > spec.maxBytes) problems.push({ code: "too_large", bytes: facts.bytes, maxBytes: spec.maxBytes })
  if (facts.width === null || facts.height === null) {
    if (facts.isWebp) problems.push({ code: "undecodable" })
  } else if (facts.width !== spec.width || facts.height !== spec.height) {
    problems.push({ code: "wrong_dimensions", width: facts.width, height: facts.height, expectedWidth: spec.width, expectedHeight: spec.height })
  }
  return problems
}

export function describeMediaProblem(p: MediaProblem, fmt: (n: number) => string): string {
  switch (p.code) {
    case "not_webp":
      return "Must be a WebP image."
    case "undecodable":
      return "Could not read the image."
    case "wrong_dimensions":
      return `Must be exactly ${p.expectedWidth}x${p.expectedHeight} px (this is ${p.width}x${p.height}).`
    case "too_large":
      return `Must be at most ${fmt(p.maxBytes)} (this is ${fmt(p.bytes)}).`
  }
}

export interface CropRect {
  sx: number
  sy: number
  sw: number
  sh: number
}

/**
 * Source rectangle for a "cover" fit of a src image into dst. `zoom` >= 1 crops further in;
 * `focusX/focusY` (0..1) pick where the crop sits inside the free space.
 */
export function coverRect(srcW: number, srcH: number, dstW: number, dstH: number, zoom = 1, focusX = 0.5, focusY = 0.5): CropRect {
  const z = Math.max(1, zoom)
  const scale = Math.max(dstW / srcW, dstH / srcH)
  const sw = Math.min(srcW, dstW / scale / z)
  const sh = Math.min(srcH, dstH / scale / z)
  const clamp = (v: number) => Math.min(1, Math.max(0, v))
  return { sx: (srcW - sw) * clamp(focusX), sy: (srcH - sh) * clamp(focusY), sw, sh }
}

export interface QualitySearch {
  quality: number
  bytes: number
  fits: boolean
}

/**
 * Finds the highest quality whose encoded size is within `cap`, by bisection (size is monotone in quality
 * for WebP in practice). `encode` returns the byte length at a given quality. When even `min` is too big,
 * returns `fits: false` at `min`.
 */
export async function findQuality(
  encode: (quality: number) => Promise<number>,
  cap: number,
  { min = 0.05, max = 0.95, steps = 7 }: { min?: number; max?: number; steps?: number } = {},
): Promise<QualitySearch> {
  const top = await encode(max)
  if (top <= cap) return { quality: max, bytes: top, fits: true }
  const bottom = await encode(min)
  if (bottom > cap) return { quality: min, bytes: bottom, fits: false }
  let lo = min
  let hi = max
  let best: QualitySearch = { quality: min, bytes: bottom, fits: true }
  for (let i = 0; i < steps; i++) {
    const mid = (lo + hi) / 2
    const bytes = await encode(mid)
    if (bytes <= cap) {
      best = { quality: mid, bytes, fits: true }
      lo = mid
    } else hi = mid
  }
  return best
}
