/**
 * Copies the built-in Drift themes and schema into src/generated and builds the token catalog.
 *
 *   DRIFT_REPO=/path/to/VideoEd pnpm --filter @cutwire/drift-theme sync
 *
 * The output is committed, so ordinary builds never need the Drift repository.
 */
import { copyFileSync, existsSync, mkdirSync, readFileSync, writeFileSync } from "node:fs"
import { dirname, join, resolve } from "node:path"
import { fileURLToPath } from "node:url"
import { flattenLeaves, isPair } from "../src/merge"
import { resolveTheme } from "../src/resolve"
import { isColorLiteral } from "../src/color"
import { GROUPS, type Category, type Json, type Library, type ThemeDoc } from "../src/types"
import type { TokenEntry, TokenType } from "../src/catalog"

const here = dirname(fileURLToPath(import.meta.url))
const pkg = resolve(here, "..")
const repo = resolve(process.env.DRIFT_REPO ?? join(pkg, "../../../../VideoEd"))
const out = join(pkg, "src/generated")

const THEMES = ["dark", "light", "graphite", "frost"]

if (!existsSync(join(repo, "resources/themes/dark.json"))) {
  console.error(`Drift repository not found at ${repo}. Set DRIFT_REPO.`)
  process.exit(1)
}
mkdirSync(out, { recursive: true })

for (const t of THEMES) copyFileSync(join(repo, `resources/themes/${t}.json`), join(out, `${t}.json`))
copyFileSync(join(repo, "resources/themes/theme.schema.json"), join(out, "theme.schema.json"))

const read = (f: string): ThemeDoc => JSON.parse(readFileSync(join(out, f), "utf8")) as ThemeDoc
const lib: Library = Object.fromEntries(THEMES.map((t) => [t, read(`${t}.json`)]))
const dark = resolveTheme(lib.dark, lib).document
const light = resolveTheme(lib.light, lib).document

const EASING = (() => {
  const names = ["Linear"]
  for (const f of ["Quad", "Cubic", "Quart", "Quint", "Sine", "Expo", "Circ", "Elastic", "Back", "Bounce"]) {
    names.push(`In${f}`, `Out${f}`, `InOut${f}`, `OutIn${f}`)
  }
  return names
})()
const BACKDROPS = ["none", "blur", "mica", "acrylic", "vibrancy"]

/** Descriptions are optional: docs/theming-tokens.md only has them if a "description" column appears. */
function readDescriptions(): Map<string, string> {
  const map = new Map<string, string>()
  const file = join(repo, "docs/theming-tokens.md")
  if (!existsSync(file)) return map
  let category = ""
  let cols: string[] = []
  for (const line of readFileSync(file, "utf8").split("\n")) {
    const h = /^## (\w+)/.exec(line)
    if (h) {
      category = h[1]
      cols = []
      continue
    }
    if (!line.startsWith("|")) continue
    const cells = line.split(/(?<!\\)\|/).slice(1, -1).map((c) => c.trim())
    if (cols.length === 0) {
      cols = cells.map((c) => c.toLowerCase())
      continue
    }
    const di = cols.indexOf("description")
    if (di < 0 || cells[di] === undefined || /^-+$/.test(cells[0])) continue
    const path = category === "components" ? `components.${cells[0]}.${cells[1]}` : `${category}.${cells[0]}`
    if (cells[di]) map.set(path, cells[di])
  }
  return map
}
const descriptions = readDescriptions()

function typeOf(path: string, v: Json | undefined): Pick<TokenEntry, "type" | "of" | "options"> {
  if (typeof v === "string") {
    if (isColorLiteral(v) || v === "transparent") return { type: "color" }
    if (path.endsWith("FontFamily")) return { type: "font" }
    if (path.startsWith("motion.easing")) return { type: "enum", options: EASING }
    if (path === "window.backdrop") return { type: "enum", options: BACKDROPS }
    return { type: "string" }
  }
  if (typeof v === "number") return { type: "number" }
  if (typeof v === "boolean") return { type: "boolean" }
  if (Array.isArray(v)) return { type: "array", of: v.every((x) => typeof x === "string") ? "color" : "number" }
  if (isPair(v)) return { type: "pair", of: "number" }
  return { type: "string" }
}

function unitOf(category: Category, path: string, type: TokenType): TokenEntry["unit"] {
  const numeric = type === "number" || type === "pair"
  if (!numeric) return undefined
  if (category === "motion") return /duration|Delay|Interval/.test(path) ? "ms" : undefined
  if (category === "metrics") return /Fraction|Seconds|Scale|Count/.test(path) ? undefined : "px"
  if (path.startsWith("typography.fontSize")) return "px"
  return undefined
}

function groupOf(category: Category, parts: string[]): string {
  switch (category) {
    case "components":
      return parts[0]
    case "palette":
      return /^[a-z]+/.exec(parts[0])?.[0] ?? parts[0]
    default:
      return parts.length > 1 ? parts[0] : "general"
  }
}

const tokens: TokenEntry[] = []
for (const category of GROUPS) {
  const rawDark = (lib.dark[category] ?? {}) as Json
  const rawLight = (lib.light[category] ?? {}) as Json
  const rd = new Map(flattenLeaves(rawDark, category))
  const rl = new Map(flattenLeaves(rawLight, category))
  const rDark = new Map(flattenLeaves(dark[category], category))
  const rLight = new Map(flattenLeaves(light[category], category))
  const paths = [...rd.keys(), ...[...rl.keys()].filter((p) => !rd.has(p))]
  for (const path of paths) {
    const t = typeOf(path, rDark.get(path) ?? rLight.get(path))
    const other = typeOf(path, rLight.get(path) ?? rDark.get(path))
    if (t.type !== other.type) console.warn(`type differs between dark and light: ${path}`)
    const parts = path.split(".").slice(1)
    const entry: TokenEntry = { path, category, group: groupOf(category, parts), ...t }
    const unit = unitOf(category, path, t.type)
    if (unit) entry.unit = unit
    if (rd.has(path)) entry.dark = rd.get(path)
    if (rl.has(path)) entry.light = rl.get(path)
    const d = descriptions.get(path)
    if (d) entry.description = d
    tokens.push(entry)
  }
}

const paletteGroups = new Map<string, number>()
for (const t of tokens) if (t.category === "palette") paletteGroups.set(t.group, (paletteGroups.get(t.group) ?? 0) + 1)
for (const t of tokens) if (t.category === "palette" && (paletteGroups.get(t.group) ?? 0) < 3) t.group = "general"

const catalog = {
  source: { files: THEMES.map((t) => `resources/themes/${t}.json`), schemaVersion: lib.dark.schemaVersion as number },
  categories: [...GROUPS],
  tokens: "@@",
}
const lines = tokens.map((t) => "    " + JSON.stringify(t)).join(",\n")
writeFileSync(
  join(out, "token-catalog.json"),
  JSON.stringify(catalog, null, 2).replace('"@@"', `[\n${lines}\n  ]`) + "\n",
)

const counts = Object.fromEntries(GROUPS.map((c) => [c, tokens.filter((t) => t.category === c).length]))
const comps = new Set(tokens.filter((t) => t.category === "components").map((t) => t.group)).size
console.log(`wrote ${tokens.length} tokens (${comps} component groups)`, counts, `from ${repo}`)
