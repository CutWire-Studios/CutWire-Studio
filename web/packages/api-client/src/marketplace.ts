import { api, apiResponse } from "./client"
import type { JobError, StockLicense, StockProvider, StockQuota } from "./stock"

export interface MarketCategory {
  id: string
  label: string
  mediaKind: string | null
  children: MarketCategory[] | null
}
export interface MarketSource {
  /** The source id's prefix: drift-assets, sfx, stock or community. */
  kind: string
  label: string
  mediaKind: string | null
  searchable: boolean
  categories: MarketCategory[] | null
  providers: StockProvider[] | null
  sorts: string[] | null
  canResolve: boolean
}
export interface MarketManifest {
  schema: number
  features: Record<string, unknown>
  sources: Record<string, MarketSource>
  /** The layout document. Validate it with the renderer before use; unknown section types must be skipped. */
  pages: unknown
}

export interface MarketVariant {
  id: string
  label: string
  width: number | null
  height: number | null
  mime: string | null
  size: number | null
}
export interface MarketItem {
  id: string
  source: string
  title: string
  subtitle: string | null
  thumbUrl: string
  previewUrl: string | null
  mediaKind: string
  durationMs: number | null
  width: number | null
  height: number | null
  tags: string[]
  license: StockLicense
  variants: MarketVariant[]
  webUrl: string | null
  category: string | null
  extra: Record<string, unknown> | null
}
export interface MarketItemPage {
  items: MarketItem[]
  nextCursor: string | null
  quota: StockQuota | null
  /** Providers a fan-out skipped, as `providerId:code`. */
  warnings?: string[] | null
}

export type MarketJobStatus = "queued" | "processing" | "ready" | "failed"
export interface MarketResolveJob {
  id: string
  status: MarketJobStatus
  item: MarketItem | null
  error: JobError | null
}
export interface InstallFile {
  role: string
  name: string
  url: string
  size: number
  sha256: string | null
  mime: string | null
}
export interface InstallJob {
  id: string
  source: string
  status: MarketJobStatus
  progress: number
  files: InstallFile[] | null
  license: StockLicense | null
  error: JobError | null
  minAppVersion: string | null
}


export interface ListItemsParams {
  category?: string
  q?: string
  cursor?: string | null
  limit?: number
  sort?: string
  period?: string
  /** A provider id, or `*` for every provider. */
  provider?: string
  /** Stock provider filters, sent as `<filterId>=<value>`. */
  filters?: Record<string, string>
}

const RESERVED = new Set(["category", "q", "cursor", "limit", "sort", "period", "provider"])

export function listQuery(p: ListItemsParams): string {
  const u = new URLSearchParams()
  for (const k of ["category", "q", "sort", "period", "provider"] as const) if (p[k]) u.set(k, p[k])
  if (p.cursor) u.set("cursor", p.cursor)
  if (p.limit) u.set("limit", String(p.limit))
  for (const [k, v] of Object.entries(p.filters ?? {})) if (v !== "" && !RESERVED.has(k)) u.set(k, v)
  const s = u.toString()
  return s ? `?${s}` : ""
}

export type ManifestResult = { notModified: true } | { notModified: false; manifest: MarketManifest; etag: string | null }

const enc = encodeURIComponent

export const marketplace = {
  /** Pass the last `etag` to get `{ notModified: true }` back when nothing changed. */
  async getManifest(etag?: string | null): Promise<ManifestResult> {
    const res = await apiResponse("/marketplace", etag ? { headers: { "If-None-Match": etag } } : {}, [304])
    if (res.status === 304) return { notModified: true }
    return { notModified: false, manifest: (await res.json()) as MarketManifest, etag: res.headers.get("ETag") }
  },
  listItems: (source: string, params: ListItemsParams = {}) =>
    api<MarketItemPage>(`/marketplace/sources/${enc(source)}/items${listQuery(params)}`),
  getItem: (source: string, id: string) => api<MarketItem>(`/marketplace/sources/${enc(source)}/items/${enc(id)}`),
  resolve: (url: string) => api<MarketResolveJob>("/marketplace/resolve", { method: "POST", body: JSON.stringify({ url }) }),
  pollResolve: (id: string) => api<MarketResolveJob>(`/marketplace/resolve/${enc(id)}`),
  install: (source: string, itemId: string, variantId?: string) =>
    api<InstallJob>("/marketplace/installs", { method: "POST", body: JSON.stringify({ source, itemId, variantId }) }),
  getInstall: (id: string) => api<InstallJob>(`/marketplace/installs/${enc(id)}`),
}
