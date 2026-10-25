import { api } from "./client"

export type StockMediaKind = "video" | "image" | "audio" | "music" | "sfx" | "overlay"
export type StockDelivery = "download" | "stream"

export interface StockFilterOption {
  id: string
  label: string
}
export interface StockFilter {
  id: string
  label: string
  kind: "select" | "toggle" | "range"
  options?: StockFilterOption[]
}
export interface StockQuota {
  limit: number
  window: "day" | "hour"
  remaining: number | null
}
export interface StockProvider {
  id: string
  label: string
  capabilities: string[]
  filters: StockFilter[]
  quota?: StockQuota
}
export interface StockType {
  id: string
  label: string
  mediaKind: StockMediaKind
  delivery: StockDelivery
  providers: StockProvider[]
}
export interface StockCatalog {
  types: StockType[]
}

export interface StockVariant {
  id: string
  label: string
  width: number | null
  height: number | null
  mime: string
  size: number | null
}
export interface StockLicense {
  name: string
  url: string | null
  attribution: string | null
}
export interface StockItem {
  id: string
  type: string
  provider: string
  title: string
  creator: { name: string; url: string | null } | null
  durationMs: number | null
  width: number | null
  height: number | null
  thumbUrl: string
  previewUrl: string | null
  license: StockLicense
  downloadable: boolean
  variants?: StockVariant[]
}
export interface StockPage {
  items: StockItem[]
  nextCursor: string | null
  quota: StockQuota | null
}

export type JobStatus = "queued" | "processing" | "ready" | "failed"
export interface JobError {
  code: string
  detail: string
}
export interface ResolveJob {
  id: string
  status: JobStatus
  item: StockItem | null
  error: JobError | null
}
export interface DownloadFile {
  filename: string
  mime: string
  size: number
  url: string
  expiresAt: string
  sha256: string
}
export interface DownloadJob {
  id: string
  status: JobStatus
  progress: number
  file: DownloadFile | null
  error: JobError | null
  license: StockLicense
}

export interface SfxSound {
  id: string
  title: string
  durationMs: number
  tags: string[]
  previewUrl: string
  downloadUrl: string
  size: number
}
export interface SfxCategory {
  id: string
  label: string
  sounds: SfxSound[]
}
export interface SfxLibrary {
  version: string
  categories: SfxCategory[]
}

export type DriftAssetKind = "lottie" | "face-prop" | "model3d"
export interface DriftAssetVariant {
  id: string
  label: string
  downloadUrl: string
  size: number
}
export interface DriftAsset {
  id: string
  title: string
  kind: DriftAssetKind
  thumbnailUrl: string
  previewUrl: string | null
  variants: DriftAssetVariant[]
}
export interface DriftAssetCategory {
  id: string
  label: string
  assets: DriftAsset[]
}
export interface DriftAssetLibrary {
  version: string
  categories: DriftAssetCategory[]
}

export interface StockSearchParams {
  type: string
  provider?: string
  q?: string
  cursor?: string | null
  limit?: number
  /** Provider filters, sent as `<filterId>=<value>`. */
  filters?: Record<string, string>
}

export const stock = {
  catalog: () => api<StockCatalog>("/stock/catalog"),
  search: (p: StockSearchParams) => {
    const u = new URLSearchParams()
    u.set("type", p.type)
    if (p.provider) u.set("provider", p.provider)
    if (p.q) u.set("q", p.q)
    if (p.cursor) u.set("cursor", p.cursor)
    if (p.limit) u.set("limit", String(p.limit))
    for (const [k, v] of Object.entries(p.filters ?? {})) if (v !== "") u.set(k, v)
    return api<StockPage>(`/stock/search?${u}`)
  },
  item: (id: string) => api<StockItem>(`/stock/items/${encodeURIComponent(id)}`),
  resolve: (url: string) => api<ResolveJob>("/stock/resolve", { method: "POST", body: JSON.stringify({ url }) }),
  resolveJob: (id: string) => api<ResolveJob>(`/stock/resolve/${encodeURIComponent(id)}`),
  download: (itemId: string, variantId?: string) =>
    api<DownloadJob>("/stock/downloads", { method: "POST", body: JSON.stringify({ itemId, variantId }) }),
  downloadJob: (id: string) => api<DownloadJob>(`/stock/downloads/${encodeURIComponent(id)}`),
  sfx: () => api<SfxLibrary>("/stock/sfx"),
  driftAssets: () => api<DriftAssetLibrary>("/stock/drift-assets"),
}
