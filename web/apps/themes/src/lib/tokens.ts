import { catalog, findToken, flattenLeaves, getPath, isObject, type Json, type ThemeDoc, type TokenEntry, type Variant } from "@cutwire/drift-theme"

/** Walks up from a leaf path to the catalog token that owns it (`metrics.controlHeight.touch` -> `metrics.controlHeight`). */
export function tokenPathFor(leaf: string): string | null {
  let p = leaf
  for (;;) {
    if (findToken(p)) return p
    const i = p.lastIndexOf(".")
    if (i < 0) return null
    p = p.slice(0, i)
  }
}

/** Catalog token paths the user has overridden: in the body, or in the overlay of the side being edited. */
export function modifiedPaths(doc: ThemeDoc, side: Variant): Set<string> {
  const out = new Set<string>()
  const add = (root: Json | undefined) => {
    if (!isObject(root)) return
    for (const group of catalog.categories) {
      for (const [leaf] of flattenLeaves(root[group], group)) {
        const t = tokenPathFor(leaf)
        if (t) out.add(t)
      }
    }
  }
  add(doc)
  add(getPath(doc, `variants.${side}`))
  return out
}

export function ownValue(doc: ThemeDoc, side: Variant, path: string): Json | undefined {
  const dual = isObject(doc.variants) && "dark" in doc.variants && "light" in doc.variants
  const overlay = dual ? getPath(doc, `variants.${side}.${path}`) : undefined
  return overlay ?? getPath(doc, path)
}

export function isRef(v: Json | undefined): v is string {
  return typeof v === "string" && v.trim().startsWith("$")
}

/** Raw reference strings that point at `path` (or inside it), as `[tokenPath, rawValue]`. */
export function referrersOf(merged: Json, path: string): string[] {
  const out: string[] = []
  const exact = `$${path}`
  for (const [leaf, v] of flattenLeaves(merged)) {
    const strings = Array.isArray(v) ? v : [v]
    if (strings.some((s) => typeof s === "string" && (s.trim() === exact || s.trim().startsWith(`${exact}.`)))) {
      const t = tokenPathFor(leaf)
      if (t && t !== path && !out.includes(t)) out.push(t)
    }
  }
  return out
}

/** Would pointing `self` at `candidate` close a loop (candidate -> ... -> self)? */
export function wouldCycle(merged: Json, self: string, candidate: string): boolean {
  let cur = candidate
  for (let i = 0; i < 64; i++) {
    if (cur === self || cur.startsWith(`${self}.`)) return true
    let v = getPath(merged, cur)
    if (Array.isArray(v)) v = undefined
    if (typeof v !== "string" || !v.trim().startsWith("$")) return false
    cur = v.trim().slice(1)
  }
  return true
}

const REF_SOURCES = new Set(["palette", "semantic", "metrics", "typography", "motion"])

/** Tokens a reference from `token` may point at: same kind of value, from the shared tiers. */
export function compatibleTargets(token: TokenEntry): TokenEntry[] {
  return catalog.tokens.filter(
    (t) => t.path !== token.path && REF_SOURCES.has(t.category) && t.type === token.type && (t.of ?? null) === (token.of ?? null),
  )
}

export function matchesQuery(t: TokenEntry, q: string): boolean {
  const hay = `${t.path} ${t.group}`.toLowerCase()
  return q
    .toLowerCase()
    .split(/\s+/)
    .filter(Boolean)
    .every((w) => hay.includes(w))
}

export function leafName(path: string, token: TokenEntry): string {
  const skip = token.category === "components" ? 2 : 1
  return path.split(".").slice(skip).join(".") || path
}
