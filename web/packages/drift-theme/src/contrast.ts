import { parseColor, type Rgba } from "./color"

const lin = (v: number) => {
  const s = v / 255
  return s <= 0.03928 ? s / 12.92 : ((s + 0.055) / 1.055) ** 2.4
}

export function relativeLuminance(c: Rgba): number {
  return 0.2126 * lin(c.r) + 0.7152 * lin(c.g) + 0.0722 * lin(c.b)
}

/** Source-over compositing of `top` onto an opaque `bottom`; the result is opaque. */
export function composite(top: Rgba, bottom: Rgba): Rgba {
  const a = top.a / 255
  return {
    r: top.r * a + bottom.r * (1 - a),
    g: top.g * a + bottom.g * (1 - a),
    b: top.b * a + bottom.b * (1 - a),
    a: 255,
  }
}

type ColorInput = string | Rgba

function toRgba(c: ColorInput): Rgba | null {
  return typeof c === "string" ? parseColor(c) : c
}

/**
 * WCAG 2 contrast ratio (1..21). Translucent colours are flattened first: the background onto
 * `under` (what shows through the window, opaque black unless told otherwise), then the
 * foreground onto that. Returns `null` when either side is not a colour.
 */
export function wcagContrast(fg: ColorInput, bg: ColorInput, under: ColorInput = "#000000"): number | null {
  const f = toRgba(fg)
  const b = toRgba(bg)
  const u = toRgba(under)
  if (!f || !b || !u) return null
  const solidBg = composite(b, composite(u, { r: 0, g: 0, b: 0, a: 255 }))
  const solidFg = composite(f, solidBg)
  const l1 = relativeLuminance(solidFg)
  const l2 = relativeLuminance(solidBg)
  return (Math.max(l1, l2) + 0.05) / (Math.min(l1, l2) + 0.05)
}

export type ContrastLevel = "aaa" | "aa" | "fail"

/** Normal text needs 4.5:1 (AA) or 7:1 (AAA); large text needs 3:1 or 4.5:1. */
export function contrastLevel(ratio: number, large = false): ContrastLevel {
  if (ratio >= (large ? 4.5 : 7)) return "aaa"
  if (ratio >= (large ? 3 : 4.5)) return "aa"
  return "fail"
}
