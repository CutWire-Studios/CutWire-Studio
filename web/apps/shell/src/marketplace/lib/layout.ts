export interface SourceRef {
  id: string
  category?: string
  q?: string
  sort?: string
  period?: string
  provider?: string
  filters?: Record<string, string>
}

export type Action =
  | { type: "open-page"; page: string }
  | { type: "open-item"; source: string; id: string }
  | { type: "open-url"; url: string }

interface Base {
  id: string
}
export interface HeroSection extends Base {
  type: "hero"
  title: string
  subtitle?: string
  image?: string
  action?: Action
}
export interface NoticeSection extends Base {
  type: "notice"
  text: string
  tone?: "info" | "warning"
}
export interface ShelfSection extends Base {
  type: "shelf"
  title: string
  limit?: number
  seeAll?: { page: string }
  source: SourceRef
}
export interface GridSection extends Base {
  type: "grid"
  title?: string
  searchable?: boolean
  source: SourceRef
}
export interface ChipsSection extends Base {
  type: "chips"
  target: string
  source: SourceRef
}
export interface StockBrowserSection extends Base {
  type: "stock-browser"
  source: SourceRef
}
export interface SfxBrowserSection extends Base {
  type: "sfx-browser"
  source: SourceRef
}
export interface DownloadsSection extends Base {
  type: "downloads"
}

export type Section =
  | HeroSection
  | NoticeSection
  | ShelfSection
  | GridSection
  | ChipsSection
  | StockBrowserSection
  | SfxBrowserSection
  | DownloadsSection
export type SectionType = Section["type"]

export interface Page {
  id: string
  title: string
  titles?: Record<string, string>
  icon?: string
  sections: Section[]
}

export const DOWNLOADS_PAGE = "downloads"

const SOURCE_BOUND = new Set<SectionType>(["shelf", "grid", "chips", "stock-browser", "sfx-browser"])

const isObject = (v: unknown): v is Record<string, unknown> => typeof v === "object" && v !== null && !Array.isArray(v)
const isString = (v: unknown): v is string => typeof v === "string" && v !== ""

function readSection(raw: unknown): Section | null {
  if (!isObject(raw) || !isString(raw.id) || !isString(raw.type)) return null
  const type = raw.type as SectionType
  if (SOURCE_BOUND.has(type) && !(isObject(raw.source) && isString(raw.source.id))) return null
  switch (type) {
    case "hero":
      return isString(raw.title) ? (raw as unknown as HeroSection) : null
    case "notice":
      return isString(raw.text) ? (raw as unknown as NoticeSection) : null
    case "shelf":
      return isString(raw.title) ? (raw as unknown as ShelfSection) : null
    case "chips":
      return isString(raw.target) ? (raw as unknown as ChipsSection) : null
    case "grid":
    case "stock-browser":
    case "sfx-browser":
    case "downloads":
      return raw as unknown as Section
    default:
      return null
  }
}

/**
 * Keeps only what this client can render: unknown section types, sections whose source is missing,
 * chips without a surviving target grid, empty pages and links to pages that were dropped.
 */
export function pruneLayout(raw: unknown, sources: Record<string, unknown>): Page[] {
  if (!Array.isArray(raw)) return []
  const pages: Page[] = []
  for (const p of raw) {
    if (!isObject(p) || !isString(p.id) || !isString(p.title) || !Array.isArray(p.sections)) continue
    const kept = p.sections
      .map(readSection)
      .filter((s): s is Section => s !== null)
      .filter((s) => !("source" in s) || s.source.id in sources)
    const grids = new Set(kept.filter((s) => s.type === "grid").map((s) => s.id))
    const sections = kept.filter((s) => s.type !== "chips" || grids.has(s.target))
    if (sections.length) pages.push({ ...(p as unknown as Page), sections })
  }
  const ids = new Set(pages.map((p) => p.id))
  const live = (a: Action | undefined) => a?.type !== "open-page" || ids.has(a.page)
  return pages
    .map((p) => ({
      ...p,
      sections: p.sections
        .map((s): Section => {
          if (s.type === "shelf" && s.seeAll && !ids.has(s.seeAll.page)) return { ...s, seeAll: undefined }
          if (s.type === "hero" && !live(s.action)) return { ...s, action: undefined }
          return s
        })
    }))
    .filter((p) => p.sections.length)
}

/** `titles["pt-BR"]`, then `titles["pt"]`, then the plain title. */
export function pageTitle(page: Pick<Page, "title" | "titles">, lang: string): string {
  const titles = Object.fromEntries(Object.entries(page.titles ?? {}).map(([k, v]) => [k.toLowerCase().replace("_", "-"), v]))
  const tag = lang.toLowerCase().replace("_", "-")
  return titles[tag] ?? titles[tag.split("-")[0]!] ?? page.title
}

/** Base64 of 64 bytes (0 silent, 255 full scale) as bar heights between 0 and 1. */
export function decodePeaks(peaks: unknown): number[] | null {
  if (typeof peaks !== "string" || peaks === "") return null
  try {
    const bin = atob(peaks)
    if (bin.length === 0) return null
    return Array.from(bin, (c) => c.charCodeAt(0) / 255)
  } catch {
    return null
  }
}

export type LinkTarget = { kind: "internal"; path: string } | { kind: "external"; url: string } | { kind: "blocked" }

/** Same-origin links stay in the app; other https links are external; everything else is refused. */
export function resolveLink(url: string, origin: string): LinkTarget {
  let u: URL
  try {
    u = new URL(url, origin)
  } catch {
    return { kind: "blocked" }
  }
  if (u.origin === origin) return { kind: "internal", path: u.pathname + u.search + u.hash }
  return u.protocol === "https:" ? { kind: "external", url: u.href } : { kind: "blocked" }
}

export const pagePath = (id: string) => `/marketplace/${encodeURIComponent(id)}`
export const itemPath = (source: string, id: string) => `/marketplace/item/${encodeURIComponent(source)}/${encodeURIComponent(id)}`

const STOCK_PAGES = new Set(["video", "photo", "audio", "music", "greenscreen"])

/** Where an old `/stock/...` URL now lives. Old item ids carry no source, so item links land on the marketplace home. */
export function legacyStockTarget(pathname: string, search: string): string {
  const [head] = pathname.split("/").filter(Boolean).slice(1)
  if (head === "sfx") return "/marketplace/sfx"
  if (head === "drift-assets") return "/marketplace/assets"
  if (head === "search") {
    const params = new URLSearchParams(search)
    const type = params.get("type") ?? ""
    if (!STOCK_PAGES.has(type)) return "/marketplace"
    params.delete("type")
    const qs = params.toString()
    return `/marketplace/${type}${qs ? `?${qs}` : ""}`
  }
  return "/marketplace"
}
