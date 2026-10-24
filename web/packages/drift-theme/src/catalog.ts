import raw from "./generated/token-catalog.json"
import type { Category, Json } from "./types"

export type TokenType = "color" | "number" | "pair" | "enum" | "font" | "string" | "boolean" | "array"

export interface TokenEntry {
  /** Full dotted path in the theme document, e.g. `components.themedButton.background`. */
  path: string
  category: Category
  /** Palette: leading word of the key. Semantic/metrics/typography: first segment. Components: the component. */
  group: string
  type: TokenType
  /** For `pair` (pointer/touch) and `array`, the type of the elements. */
  of?: "color" | "number"
  /** Choices for `enum` tokens. */
  options?: string[]
  unit?: "px" | "ms"
  /** Built-in dark and light values as written (references unresolved); absent when the theme lacks the token. */
  dark?: Json
  light?: Json
  description?: string
}

export interface TokenCatalog {
  source: { files: string[]; schemaVersion: number }
  categories: Category[]
  tokens: TokenEntry[]
}

export const catalog = raw as unknown as TokenCatalog

const byPath = new Map<string, TokenEntry>(catalog.tokens.map((t) => [t.path, t]))

export function findToken(path: string): TokenEntry | undefined {
  return byPath.get(path)
}

export function tokensIn(category: Category): TokenEntry[] {
  return catalog.tokens.filter((t) => t.category === category)
}

export interface GroupInfo {
  category: Category
  group: string
  count: number
}

export const groups: GroupInfo[] = (() => {
  const m = new Map<string, GroupInfo>()
  for (const t of catalog.tokens) {
    const k = `${t.category}/${t.group}`
    const g = m.get(k)
    if (g) g.count++
    else m.set(k, { category: t.category, group: t.group, count: 1 })
  }
  return [...m.values()]
})()

export function groupsIn(category: Category): GroupInfo[] {
  return groups.filter((g) => g.category === category)
}
