export interface WebpFit {
  blob: Blob
  quality: number
  bytes: number
  /** false when even the lowest quality is over the cap */
  fits: boolean
}

export interface FitOptions {
  min?: number
  max?: number
  /** binary-search rounds between `min` and `max` */
  rounds?: number
}

/**
 * Finds the highest WebP quality whose encoding is at most `cap` bytes: the best quality if it
 * already fits, otherwise a binary search. The encoder is injected so this stays pure.
 */
export async function fitWebp(encode: (quality: number) => Promise<Blob>, cap: number, opts: FitOptions = {}): Promise<WebpFit> {
  const { min = 0.05, max = 0.95, rounds = 7 } = opts
  const run = async (quality: number): Promise<WebpFit> => {
    const blob = await encode(quality)
    return { blob, quality, bytes: blob.size, fits: blob.size <= cap }
  }
  const top = await run(max)
  if (top.fits) return top
  const bottom = await run(min)
  if (!bottom.fits) return bottom
  let best = bottom
  let lo = min
  let hi = max
  for (let i = 0; i < rounds; i++) {
    const mid = (lo + hi) / 2
    const r = await run(mid)
    if (r.fits) {
      best = r
      lo = mid
    } else hi = mid
  }
  return best
}

export const KIND_MEDIA = {
  thumbnail: { width: 512, height: 288, cap: 40 * 1024 },
  strip: { width: 1536, height: 128, cap: 80 * 1024 },
} as const
