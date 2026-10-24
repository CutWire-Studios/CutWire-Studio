import { unzipSync, zipSync, strFromU8, strToU8 } from "fflate"
import { clone, isObject, mergeTheme } from "./merge"
import { GROUPS, MAX_FILE_BYTES, MAX_TOTAL_BYTES, MAX_ZIP_ENTRIES, SCHEMA_VERSION, type JsonObject, type ThemeDoc } from "./types"

export class ThemeExportError extends Error {
  constructor(message: string) {
    super(message)
    this.name = "ThemeExportError"
  }
}

export class ThemeImportError extends Error {
  constructor(
    message: string,
    readonly code: "qml" | "unsafe-path" | "limits" | "invalid" | "unsupported",
  ) {
    super(message)
    this.name = "ThemeImportError"
  }
}

const ORDER = ["name", "schemaVersion", "extends", "variant", "variants", "window", ...GROUPS.filter((g) => g !== "window")]
const META = ["name", "extends", "variant", "variants"]

/** Stable key order: identity first, then window and the token groups; unknown keys last. */
function ordered(doc: ThemeDoc): JsonObject {
  const out: JsonObject = {}
  for (const k of ORDER) if (k in doc) out[k] = doc[k]
  for (const k of Object.keys(doc)) if (!(k in out)) out[k] = doc[k]
  return out
}

function serialize(doc: ThemeDoc): string {
  const text = JSON.stringify(doc, null, 2) + "\n"
  if (strToU8(text).length > MAX_FILE_BYTES) throw new ThemeExportError("theme is larger than 2 MB")
  return text
}

/** The text of `theme.json`: schemaVersion is at least 1, keys are in a fixed order. */
export function exportJson(doc: ThemeDoc): string {
  const sv = typeof doc.schemaVersion === "number" ? Math.max(Math.trunc(doc.schemaVersion), SCHEMA_VERSION) : SCHEMA_VERSION
  return serialize(ordered({ ...doc, schemaVersion: sv }))
}

/**
 * Moves inline `variants` into side files so a zip uses the `theme.dark.json` / `theme.light.json`
 * layout. Returns the document without `variants`.
 */
export function splitVariants(doc: ThemeDoc): { doc: ThemeDoc; sides: { dark?: ThemeDoc; light?: ThemeDoc } } {
  const { variants, ...rest } = doc
  const sides: { dark?: ThemeDoc; light?: ThemeDoc } = {}
  if (isObject(variants)) {
    if (isObject(variants.dark)) sides.dark = variants.dark
    if (isObject(variants.light)) sides.light = variants.light
  }
  return { doc: rest, sides }
}

/**
 * The package Drift imports: `theme.json` and, when given, `theme.dark.json` / `theme.light.json`
 * (partial documents without name/extends/variant/variants). Never contains a `qml/` folder.
 */
export function exportZip(doc: ThemeDoc, sides: { dark?: ThemeDoc; light?: ThemeDoc } = {}): Uint8Array {
  const files: Record<string, Uint8Array> = { "theme.json": strToU8(exportJson(doc)) }
  for (const side of ["dark", "light"] as const) {
    const body = sides[side]
    if (!body) continue
    const bad = META.find((k) => k in body)
    if (bad) throw new ThemeExportError(`theme.${side}.json cannot contain "${bad}"`)
    files[`theme.${side}.json`] = strToU8(serialize(body))
  }
  const names = Object.keys(files)
  if (names.length > MAX_ZIP_ENTRIES) throw new ThemeExportError("too many entries")
  const total = names.reduce((n, k) => n + files[k].length, 0)
  if (total > MAX_TOTAL_BYTES) throw new ThemeExportError("theme is larger than 8 MB")
  const mtime = new Date(2024, 0, 1)
  const zipInput: Record<string, [Uint8Array, { level: 9; mtime: Date }]> = {}
  for (const k of names) zipInput[k] = [files[k], { level: 9, mtime }]
  const zipped = zipSync(zipInput)
  if (zipped.length > MAX_TOTAL_BYTES) throw new ThemeExportError("theme is larger than 8 MB")
  return zipped
}

const junk = (path: string) => {
  if (path.startsWith("__MACOSX/")) return true
  const base = path.slice(path.lastIndexOf("/") + 1)
  return base === ".DS_Store" || base.startsWith("._")
}

function parseObject(text: string, what: string): JsonObject {
  let value: unknown
  try {
    value = JSON.parse(text)
  } catch (e) {
    throw new ThemeImportError(`${what} is not valid JSON: ${(e as Error).message}`, "invalid")
  }
  if (!isObject(value)) throw new ThemeImportError(`${what} must be a JSON object`, "invalid")
  return value
}

function readZip(bytes: Uint8Array): { doc: ThemeDoc; sides: { dark?: ThemeDoc; light?: ThemeDoc } } {
  if (bytes.length > MAX_TOTAL_BYTES) throw new ThemeImportError("theme archive is too large", "limits")
  const entries: Array<{ name: string; size: number; originalSize: number }> = []
  try {
    unzipSync(bytes, {
      filter(f) {
        entries.push({ name: f.name, size: f.size, originalSize: f.originalSize })
        return false
      },
    })
  } catch {
    throw new ThemeImportError("not a valid ZIP archive", "invalid")
  }
  if (entries.length > MAX_ZIP_ENTRIES) throw new ThemeImportError("theme archive has too many entries", "limits")

  let declared = 0
  for (const e of entries) {
    const { name } = e
    if (!name) throw new ThemeImportError("theme archive contains an empty path", "unsafe-path")
    if (name.startsWith("/") || name.includes("..") || name.includes(":") || name.includes("\\")) {
      throw new ThemeImportError(`theme archive path is not allowed: ${name}`, "unsafe-path")
    }
    if (name.endsWith("/") || junk(name)) continue
    if (e.size > MAX_FILE_BYTES || e.originalSize > MAX_FILE_BYTES) {
      throw new ThemeImportError(`theme archive entry is too large: ${name}`, "limits")
    }
    declared += e.originalSize
    if (declared > MAX_TOTAL_BYTES) throw new ThemeImportError("theme archive is too large", "limits")
  }
  const files = entries.filter((e) => !e.name.endsWith("/") && !junk(e.name)).map((e) => e.name)
  if (files.some((n) => n.split("/").includes("qml") || n.toLowerCase().endsWith(".qml"))) {
    throw new ThemeImportError("QML overrides are code and are not accepted; remove the qml/ folder", "qml")
  }

  let prefix = ""
  const slash = files[0]?.indexOf("/") ?? -1
  if (slash > 0) {
    const candidate = files[0].slice(0, slash + 1)
    if (files.every((n) => n.startsWith(candidate))) prefix = candidate
  }
  const relative = (n: string) => n.slice(prefix.length)
  const hasThemeJson = files.some((n) => relative(n) === "theme.json")
  const wanted = new Map<string, string>()
  for (const n of files) {
    const r = relative(n)
    if (r === "theme.json" || r === "theme.dark.json" || r === "theme.light.json") wanted.set(n, r)
    else if (!hasThemeJson && !r.includes("/") && r.toLowerCase().endsWith(".json")) {
      if ([...wanted.values()].includes("theme.json")) throw new ThemeImportError("archive has more than one theme file", "invalid")
      wanted.set(n, "theme.json")
    }
  }
  if (![...wanted.values()].includes("theme.json")) throw new ThemeImportError("archive does not contain a theme", "invalid")

  const data = unzipSync(bytes, { filter: (f) => wanted.has(f.name) })
  let total = 0
  const texts = new Map<string, string>()
  for (const [name, rel] of wanted) {
    const buf = data[name]
    if (!buf) continue
    total += buf.length
    if (buf.length > MAX_FILE_BYTES || total > MAX_TOTAL_BYTES) throw new ThemeImportError("theme archive is too large", "limits")
    texts.set(rel, strFromU8(buf))
  }
  const doc = parseObject(texts.get("theme.json") ?? "", "theme.json")
  const sides: { dark?: ThemeDoc; light?: ThemeDoc } = {}
  for (const side of ["dark", "light"] as const) {
    const t = texts.get(`theme.${side}.json`)
    if (t === undefined) continue
    const body = parseObject(t, `theme.${side}.json`)
    const bad = META.find((k) => k in body)
    if (bad) throw new ThemeImportError(`theme.${side}.json cannot contain "${bad}"`, "invalid")
    sides[side] = body
  }
  return { doc, sides }
}

/**
 * Reads a `.json` or `.zip` theme into a single document (side files are folded into
 * `variants`). Refuses anything with QML, unsafe paths, or beyond Drift's size and entry limits.
 */
export function importTheme(fileName: string, bytes: Uint8Array): ThemeDoc {
  const isZip = /\.zip$/i.test(fileName) || (bytes.length > 3 && bytes[0] === 0x50 && bytes[1] === 0x4b && bytes[2] === 3 && bytes[3] === 4)
  if (!isZip) {
    if (!/\.json$/i.test(fileName) && !/^\s*\{/.test(strFromU8(bytes.subarray(0, 64)))) {
      throw new ThemeImportError("theme import must be a .json file or a .zip", "unsupported")
    }
    if (bytes.length > MAX_FILE_BYTES) throw new ThemeImportError("theme file is too large", "limits")
    return parseObject(strFromU8(bytes), "theme file")
  }
  const { doc, sides } = readZip(bytes)
  if (!sides.dark && !sides.light) return doc
  const folded: JsonObject = isObject(doc.variants) ? clone(doc.variants) : {}
  for (const side of ["dark", "light"] as const) {
    if (sides[side]) folded[side] = mergeTheme(folded[side], sides[side])
  }
  return { ...doc, variants: folded }
}
