export interface Rgba {
  r: number
  g: number
  b: number
  /** 0..255 */
  a: number
}

const HEX = /^#([0-9A-Fa-f]{3}|[0-9A-Fa-f]{4}|[0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$/

export function isColorLiteral(text: string): boolean {
  return HEX.test(text)
}

const h = (s: string) => Number.parseInt(s, 16)
const h1 = (c: string) => h(c + c)

/**
 * Parses a Drift colour. Eight digits are Qt order (`#AARRGGBB`, alpha first); CSS puts alpha last,
 * so these two must never be mixed up. Four digits are read as `#ARGB`, the same alpha-first rule.
 * `transparent` is accepted because built-in component tokens use it.
 */
export function parseColor(input: string): Rgba | null {
  const text = input.trim()
  if (text.toLowerCase() === "transparent") return { r: 0, g: 0, b: 0, a: 0 }
  const m = HEX.exec(text)
  if (!m) return null
  const d = m[1]
  switch (d.length) {
    case 3:
      return { r: h1(d[0]), g: h1(d[1]), b: h1(d[2]), a: 255 }
    case 4:
      return { a: h1(d[0]), r: h1(d[1]), g: h1(d[2]), b: h1(d[3]) }
    case 6:
      return { r: h(d.slice(0, 2)), g: h(d.slice(2, 4)), b: h(d.slice(4, 6)), a: 255 }
    default:
      return { a: h(d.slice(0, 2)), r: h(d.slice(2, 4)), g: h(d.slice(4, 6)), b: h(d.slice(6, 8)) }
  }
}

const byte = (n: number) => Math.max(0, Math.min(255, Math.round(n)))
const hex2 = (n: number) => byte(n).toString(16).padStart(2, "0")

/** `qt` is what goes in theme files (`#rrggbb`, or `#aarrggbb` when not opaque); `css` is `rgba()`. */
export function formatColor(c: Rgba, format: "qt" | "css" = "qt"): string {
  if (format === "css") {
    const alpha = Math.round((byte(c.a) / 255) * 1000) / 1000
    return `rgba(${byte(c.r)}, ${byte(c.g)}, ${byte(c.b)}, ${alpha})`
  }
  const rgb = hex2(c.r) + hex2(c.g) + hex2(c.b)
  return byte(c.a) === 255 ? `#${rgb}` : `#${hex2(c.a)}${rgb}`
}

/** Converts a theme colour string to CSS; anything that is not a colour is returned unchanged. */
export function toCss(text: string): string {
  const c = parseColor(text)
  return c ? formatColor(c, "css") : text
}

/** Reads CSS `rgb()/rgba()` or `#rrggbb` / `#rrggbbaa` (alpha last, as CSS defines it). */
export function parseCssColor(input: string): Rgba | null {
  const text = input.trim()
  const fn = /^rgba?\(\s*([\d.]+)[\s,]+([\d.]+)[\s,]+([\d.]+)(?:[\s,/]+([\d.]+%?))?\s*\)$/i.exec(text)
  if (fn) {
    const a = fn[4] === undefined ? 1 : fn[4].endsWith("%") ? Number.parseFloat(fn[4]) / 100 : Number.parseFloat(fn[4])
    return { r: byte(+fn[1]), g: byte(+fn[2]), b: byte(+fn[3]), a: byte(a * 255) }
  }
  const m = /^#([0-9A-Fa-f]{8})$/.exec(text)
  if (m) {
    const d = m[1]
    return { r: h(d.slice(0, 2)), g: h(d.slice(2, 4)), b: h(d.slice(4, 6)), a: h(d.slice(6, 8)) }
  }
  return parseColor(text)
}
