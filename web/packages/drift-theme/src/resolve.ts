import { isColorLiteral, parseColor } from "./color"
import { getPath, isObject, mergeTheme } from "./merge"
import { SCHEMA_VERSION, type Issue, type Json, type JsonObject, type Library, type ThemeDoc, type Variant } from "./types"

/** A reference chain longer than this is treated as a cycle, as in ThemeManager.cpp. */
export const MAX_REFERENCE_DEPTH = 64

const REF = /^\$[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*$/

export function isReference(text: string): boolean {
  return REF.test(text)
}

export type ResolveIssueKind = "cycle" | "unresolved" | "malformed" | "invalid-color"

export interface ResolveIssue extends Issue {
  kind: ResolveIssueKind
  /** The reference involved, without the `$`. */
  ref?: string
}

export interface ResolveRefsResult {
  value: Json
  issues: ResolveIssue[]
}

/**
 * Resolves every `$a.b.c` string in `root` against `root` itself. Strings are trimmed. A cycle, a
 * chain deeper than 64, an unknown path or a malformed reference leaves the original string in place
 * and is reported. Nothing throws.
 */
export function resolveRefs(root: Json): ResolveRefsResult {
  const issues: ResolveIssue[] = []
  const seen = new Set<string>()
  const report = (kind: ResolveIssueKind, path: string, ref: string | undefined, message: string) => {
    const key = `${kind}|${path}|${ref ?? ""}`
    if (seen.has(key)) return
    seen.add(key)
    issues.push({ kind, path, ref, message, code: kind })
  }

  const walk = (value: Json, path: string, stack: string[]): Json => {
    if (typeof value === "string") {
      const text = value.trim()
      if (!text.startsWith("$")) return text
      if (!isReference(text)) {
        report("malformed", path, undefined, `malformed reference "${text}"`)
        return text
      }
      const ref = text.slice(1)
      if (stack.includes(ref) || stack.length >= MAX_REFERENCE_DEPTH) {
        report("cycle", path, ref, `reference cycle involving $${ref}`)
        return text
      }
      const target = getPath(root, ref)
      if (target === undefined) {
        report("unresolved", path, ref, `unresolved reference $${ref}`)
        return text
      }
      stack.push(ref)
      const resolved = walk(target, ref, stack)
      stack.pop()
      return resolved
    }
    if (Array.isArray(value)) return value.map((v, i) => walk(v, `${path}[${i}]`, stack))
    if (isObject(value)) {
      const out: JsonObject = {}
      for (const k of Object.keys(value)) out[k] = walk(value[k], path ? `${path}.${k}` : k, stack)
      return out
    }
    return value
  }

  return { value: walk(root, "", []), issues }
}

/** Reports colour-looking strings (`#...`) that are not valid colour literals. */
export function coerceIssues(root: Json, path = "", out: ResolveIssue[] = []): ResolveIssue[] {
  if (typeof root === "string") {
    if (root.startsWith("#") && (!isColorLiteral(root) || !parseColor(root))) {
      out.push({ kind: "invalid-color", path, message: `invalid color "${root}"`, code: "invalid-color" })
    }
  } else if (Array.isArray(root)) {
    root.forEach((v, i) => coerceIssues(v, `${path}[${i}]`, out))
  } else if (isObject(root)) {
    for (const k of Object.keys(root)) coerceIssues(root[k], path ? `${path}.${k}` : k, out)
  }
  return out
}

export class ThemeError extends Error {
  constructor(
    message: string,
    readonly code: "unknown-base" | "extends-cycle" | "no-defaults",
  ) {
    super(message)
    this.name = "ThemeError"
  }
}

export interface MergeOptions {
  /** Which side to show when the theme ships both dark and light overlays. Defaults to dark. */
  variant?: Variant
  /** `theme.dark.json` / `theme.light.json` contents from a folder or zip theme. */
  sides?: { dark?: ThemeDoc; light?: ThemeDoc }
  /** Used when the document has no usable `name`. */
  fallbackName?: string
}

export interface MergedTheme {
  /** The final document with references still in place. */
  merged: JsonObject
  /** Which side is on screen. */
  variant: Variant
  /** True when both overlays exist, so the light/dark toggle keeps the theme selected. */
  hasVariants: boolean
  schemaVersion: number
  name: string
}

function compose(doc: ThemeDoc, lib: Library, stack: string[]): JsonObject {
  const name = typeof doc.name === "string" ? doc.name : ""
  if (name && stack.includes(name)) throw new ThemeError(`extends cycle at ${name}`, "extends-cycle")
  const next = name ? [...stack, name] : stack
  let out: JsonObject = { ...doc }
  const ext = out.extends
  if (ext !== undefined) {
    delete out.extends
    const baseName = typeof ext === "string" ? ext : ""
    const baseDoc = Object.prototype.hasOwnProperty.call(lib, baseName) ? lib[baseName] : undefined
    if (!baseDoc) throw new ThemeError(`unknown theme "${baseName}"`, "unknown-base")
    const base = compose(baseDoc, lib, next)
    const version = Math.max(intOf(base.schemaVersion), intOf(out.schemaVersion))
    out = mergeTheme(base, out) as JsonObject
    if (version > 0) out.schemaVersion = version
  }
  return out
}

function intOf(v: Json | undefined): number {
  return typeof v === "number" ? Math.trunc(v) : 0
}

const META = ["name", "extends", "variant", "variants"]
function stripMeta(o: JsonObject): JsonObject {
  const out = { ...o }
  for (const k of META) delete out[k]
  return out
}

/**
 * The merge half of ThemeManager::readTheme: extends chain, then the variant overlay, then the
 * built-in of the same variant underneath (so tokens added in a later Drift fill in with defaults).
 * Throws `ThemeError` for an unknown or cyclic `extends`.
 */
export function mergeLayers(doc: ThemeDoc, lib: Library, opts: MergeOptions = {}): MergedTheme {
  let merged = compose(doc, lib, [])

  let darkOverlay: JsonObject = {}
  let lightOverlay: JsonObject = {}
  let hasDark = false
  let hasLight = false
  const variants = merged.variants
  delete merged.variants
  if (isObject(variants)) {
    if (isObject(variants.dark)) {
      darkOverlay = stripMeta(variants.dark)
      hasDark = true
    }
    if (isObject(variants.light)) {
      lightOverlay = stripMeta(variants.light)
      hasLight = true
    }
  }
  if (opts.sides?.dark) {
    darkOverlay = mergeTheme(darkOverlay, stripMeta(opts.sides.dark)) as JsonObject
    hasDark = true
  }
  if (opts.sides?.light) {
    lightOverlay = mergeTheme(lightOverlay, stripMeta(opts.sides.light)) as JsonObject
    hasLight = true
  }

  const hasVariants = hasDark && hasLight
  let declared: Variant | "" = merged.variant === "dark" || merged.variant === "light" ? merged.variant : ""
  if (hasVariants) {
    const side: Variant = opts.variant === "light" ? "light" : "dark"
    merged = mergeTheme(merged, side === "light" ? lightOverlay : darkOverlay) as JsonObject
    declared = side
  } else if (hasDark || hasLight) {
    merged = mergeTheme(merged, hasDark ? darkOverlay : lightOverlay) as JsonObject
    if (!declared) declared = hasDark ? "dark" : "light"
  } else if (!declared) {
    declared = "dark"
  }
  merged.variant = declared

  const schemaVersion = intOf(merged.schemaVersion)
  const isBuiltin = typeof doc.name === "string" && lib[doc.name] === doc
  if (!isBuiltin) {
    const defaults = lib[declared]
    if (!defaults) throw new ThemeError(`built-in ${declared} theme is missing`, "no-defaults")
    merged = mergeTheme(defaults, merged) as JsonObject
  }
  merged.schemaVersion = Math.max(schemaVersion, SCHEMA_VERSION)

  const own = typeof doc.name === "string" ? doc.name.trim() : ""
  const name = own || opts.fallbackName || "untitled"
  merged.name = name
  return { merged, variant: declared, hasVariants, schemaVersion, name }
}

export interface ResolvedTheme extends MergedTheme {
  /** The fully resolved document (no `$` references left unless they were broken). */
  document: JsonObject
  issues: ResolveIssue[]
}

/** Full pipeline: `mergeLayers`, then references, then colour validity. */
export function resolveTheme(doc: ThemeDoc, lib: Library, opts: MergeOptions = {}): ResolvedTheme {
  const m = mergeLayers(doc, lib, opts)
  const { value, issues } = resolveRefs(m.merged)
  return { ...m, document: value as JsonObject, issues: [...issues, ...coerceIssues(m.merged)] }
}
