export type Json = string | number | boolean | null | Json[] | { [key: string]: Json }
export type JsonObject = { [key: string]: Json }

/** A theme document as written to disk (`theme.json`), or a partial one (variant overlay / side file). */
export type ThemeDoc = JsonObject

export type Variant = "dark" | "light"

export const GROUPS = ["palette", "semantic", "components", "metrics", "typography", "motion", "window"] as const
export type Category = (typeof GROUPS)[number]

/** Built-in names plus `system`, which is a selection and not a theme; none may be used by a user theme. */
export const BUILTIN_NAMES = ["dark", "light", "graphite", "frost"] as const
export const RESERVED_NAMES = [...BUILTIN_NAMES, "system"] as const

export const SCHEMA_VERSION = 1
export const MAX_FILE_BYTES = 2 * 1024 * 1024
export const MAX_TOTAL_BYTES = 8 * 1024 * 1024
export const MAX_ZIP_ENTRIES = 64

export type Library = Readonly<Record<string, ThemeDoc>>

export interface Issue {
  /** Dotted token path, or an empty string for the whole document. */
  path: string
  message: string
  code: string
}
