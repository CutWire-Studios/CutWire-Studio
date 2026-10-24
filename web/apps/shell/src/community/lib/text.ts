export type Segment = { type: "text"; text: string } | { type: "link"; text: string; href: string }

const URL_RE = /https?:\/\/[^\s<>"']+/gi
const TRAILING = /[.,;:!?)\]]+$/

/** Splits plain text into segments, turning http(s) URLs into links. Nothing is ever interpreted as HTML. */
export function linkify(text: string): Segment[] {
  const out: Segment[] = []
  let last = 0
  for (const m of text.matchAll(URL_RE)) {
    const start = m.index ?? 0
    const raw = m[0]
    const trimmed = raw.replace(TRAILING, "")
    if (start > last) out.push({ type: "text", text: text.slice(last, start) })
    out.push({ type: "link", text: trimmed, href: trimmed })
    last = start + trimmed.length
  }
  if (last < text.length) out.push({ type: "text", text: text.slice(last) })
  return out
}

/** Paragraphs are separated by blank lines; single newlines stay inside a paragraph. */
export function paragraphs(text: string): Segment[][] {
  return text
    .replace(/\r\n?/g, "\n")
    .split(/\n\s*\n/)
    .map((p) => p.trim())
    .filter(Boolean)
    .map(linkify)
}

export function formatBytes(n: number): string {
  if (n < 1024) return `${n} B`
  if (n < 1024 * 1024) return `${(n / 1024).toFixed(n < 10240 ? 1 : 0)} KB`
  return `${(n / 1024 / 1024).toFixed(1)} MB`
}

export function compact(n: number): string {
  return new Intl.NumberFormat("en", { notation: "compact", maximumFractionDigits: 1 }).format(n)
}
