import type { ErrorObject } from "ajv"
import validateSchema from "./generated/theme.validator.js"
import { builtins } from "./builtins"
import { isColorLiteral } from "./color"
import { flattenLeaves } from "./merge"
import { resolveTheme, ThemeError, type ResolveIssue } from "./resolve"
import {
  BUILTIN_NAMES,
  MAX_FILE_BYTES,
  RESERVED_NAMES,
  SCHEMA_VERSION,
  type Issue,
  type JsonObject,
  type Library,
  type ThemeDoc,
} from "./types"

export interface ValidationResult {
  valid: boolean
  errors: Issue[]
  warnings: Issue[]
}

export interface ValidateOptions {
  /** Themes `extends` may point at. Without it only built-in names are known. */
  library?: Library
  /** Publishing needs a name; Drift itself falls back to the file name. Default true. */
  requireName?: boolean
}

const toPath = (instancePath: string) =>
  instancePath
    .split("/")
    .filter(Boolean)
    .map((p) => p.replace(/~1/g, "/").replace(/~0/g, "~"))
    .join(".")

function schemaIssues(errors: ErrorObject[]): Issue[] {
  const out: Issue[] = []
  const seen = new Set<string>()
  const oneOfPaths = errors.filter((e) => e.keyword === "oneOf").map((e) => e.instancePath)
  for (const e of errors) {
    const path = toPath(e.instancePath)
    let message: string
    if (e.keyword === "oneOf") message = "must be a string, number, boolean, array or object"
    else if (oneOfPaths.some((p) => e.instancePath.startsWith(p) && e.instancePath !== p)) continue
    else if (oneOfPaths.includes(e.instancePath)) continue
    else if (e.keyword === "additionalProperties") {
      message = `unknown key "${(e.params as { additionalProperty: string }).additionalProperty}"`
    } else if (e.keyword === "pattern" && e.schemaPath.includes("colorOrRef")) {
      message = "must be a color (#rgb, #rrggbb, #aarrggbb) or a $reference"
    } else message = e.message ?? "invalid"
    const key = `${path}|${message}`
    if (seen.has(key)) continue
    seen.add(key)
    out.push({ path, message, code: `schema.${e.keyword}` })
    if (out.length >= 100) break
  }
  return out
}

/** Mirrors `isSafeThemeId` in ThemeManager.cpp, plus the reserved names. */
export function themeNameProblem(name: string): string | null {
  if (!name.trim()) return "name is required"
  if ((RESERVED_NAMES as readonly string[]).includes(name)) return `"${name}" is a reserved name`
  if (name.startsWith(".")) return "name cannot start with a dot"
  if (name.includes("/") || name.includes("\\") || name.includes("..")) return 'name cannot contain "/", "\\" or ".."'
  if ([...name].some((ch) => ch.charCodeAt(0) < 32)) return "name cannot contain control characters"
  return null
}

function resolveProblems(doc: ThemeDoc, lib: Library): { errors: Issue[]; fatal: Issue | null } {
  const errors: Issue[] = []
  const seen = new Set<string>()
  const add = (i: ResolveIssue) => {
    const key = `${i.kind}|${i.path}|${i.ref ?? ""}`
    if (seen.has(key)) return
    seen.add(key)
    errors.push({ path: i.path, message: i.message, code: `resolve.${i.kind}` })
  }
  const variants = doc.variants && typeof doc.variants === "object" && !Array.isArray(doc.variants) ? (doc.variants as JsonObject) : {}
  const sides = (["dark", "light"] as const).filter((s) => s in variants)
  try {
    if (sides.length === 2) {
      for (const side of sides) resolveTheme(doc, lib, { variant: side }).issues.forEach(add)
    } else {
      resolveTheme(doc, lib).issues.forEach(add)
    }
  } catch (e) {
    if (e instanceof ThemeError) return { errors, fatal: { path: "extends", message: e.message, code: `extends.${e.code}` } }
    throw e
  }
  return { errors, fatal: null }
}

function fourDigitColors(doc: ThemeDoc): Issue[] {
  const out: Issue[] = []
  for (const [path, v] of flattenLeaves(doc)) {
    if (typeof v === "string" && isColorLiteral(v) && v.length === 5) {
      out.push({ path, message: `${v} is a 4-digit color; use #AARRGGBB (alpha first)`, code: "color.short-argb" })
    }
  }
  return out
}

/** Schema validation (JSON Schema 2020-12) plus the rules Drift's importer and the community site enforce. */
export function validateTheme(doc: ThemeDoc, opts: ValidateOptions = {}): ValidationResult {
  const errors: Issue[] = []
  const warnings: Issue[] = []
  const lib: Library = { ...builtins, ...opts.library }

  if (!validateSchema(doc)) errors.push(...schemaIssues(validateSchema.errors ?? []))

  if (typeof doc.name === "string") {
    const problem = themeNameProblem(doc.name)
    if (problem) errors.push({ path: "name", message: problem, code: "name" })
  } else if ((opts.requireName ?? true) && doc.name === undefined) {
    errors.push({ path: "name", message: "name is required", code: "name" })
  }

  if (typeof doc.schemaVersion === "number") {
    if (doc.schemaVersion > SCHEMA_VERSION) {
      errors.push({ path: "schemaVersion", message: `schemaVersion must be ${SCHEMA_VERSION}`, code: "schemaVersion" })
    }
  } else if (doc.schemaVersion === undefined) {
    warnings.push({ path: "schemaVersion", message: `schemaVersion is missing; ${SCHEMA_VERSION} will be written on export`, code: "schemaVersion" })
  }

  if (typeof doc.extends === "string" && !(BUILTIN_NAMES as readonly string[]).includes(doc.extends) && !(doc.extends in lib)) {
    warnings.push({ path: "extends", message: `base theme "${doc.extends}" is not built in; it must be installed in Drift`, code: "extends.unknown" })
  }

  const resolved = resolveProblems(doc, lib)
  errors.push(...resolved.errors)
  if (resolved.fatal) errors.push(resolved.fatal)
  warnings.push(...fourDigitColors(doc))

  const bytes = new TextEncoder().encode(JSON.stringify(doc)).length
  if (bytes > MAX_FILE_BYTES) errors.push({ path: "", message: "theme is larger than 2 MB", code: "size" })

  return { valid: errors.length === 0, errors, warnings }
}
