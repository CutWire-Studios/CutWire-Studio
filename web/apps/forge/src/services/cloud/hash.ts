import type { ForgeDoc } from "@cutwire/forge-core/doc/types"

function canonical(v: unknown): string {
  if (Array.isArray(v)) return `[${v.map(canonical).join(",")}]`
  if (v && typeof v === "object") {
    const o = v as Record<string, unknown>
    return `{${Object.keys(o)
      .sort()
      .filter((k) => o[k] !== undefined)
      .map((k) => `${JSON.stringify(k)}:${canonical(o[k])}`)
      .join(",")}}`
  }
  return JSON.stringify(v) ?? "null"
}

function fnv(s: string): string {
  let a = 0x811c9dc5
  let b = 0x01000193
  for (let i = 0; i < s.length; i++) {
    const c = s.charCodeAt(i)
    a = Math.imul(a ^ c, 0x01000193) >>> 0
    b = Math.imul(b + c, 0x85ebca6b) >>> 0
  }
  return `${a.toString(36)}${b.toString(36)}.${s.length}`
}

/**
 * A cheap fingerprint of a document's content. Key order doesn't matter (the server may reorder
 * keys), and pictures count by id and size rather than bytes, since they are immutable per id.
 */
export function docHash(doc: ForgeDoc): string {
  const assets = doc.assets.map((a) => [a.id, a.name, a.mime, a.width, a.height, a.data?.length ?? 0])
  return fnv(canonical({ ...doc, assets }))
}
