import { marketplace, type ListItemsParams, type MarketItem, type MarketItemPage } from "@cutwire/api-client"
import type { SourceRef } from "./layout"

const isObject = (v: unknown): v is Record<string, unknown> => typeof v === "object" && v !== null && !Array.isArray(v)

/** Stock items carry `creator` as a name or as `{ name, url }`. */
export function creatorOf(item: MarketItem): { name: string; url: string | null } | null {
  const c = item.extra?.creator
  if (typeof c === "string" && c) return { name: c, url: null }
  if (isObject(c) && typeof c.name === "string" && c.name) return { name: c.name, url: typeof c.url === "string" ? c.url : null }
  return null
}

export const providerOf = (item: MarketItem): string | null => (typeof item.extra?.provider === "string" ? item.extra.provider : null)

/** Only stock items can say they are not downloadable; everything else on the server can be installed. */
export const isDownloadable = (item: MarketItem) => item.extra?.downloadable !== false

export const isCommunity = (item: MarketItem) => item.source.startsWith("community.")

/** Layout defaults first; a defined override wins. */
export function feedParams(ref: SourceRef, over: ListItemsParams = {}): ListItemsParams {
  const out: ListItemsParams = {
    category: ref.category,
    q: ref.q,
    sort: ref.sort,
    period: ref.period,
    provider: ref.provider,
    filters: ref.filters,
  }
  for (const [k, v] of Object.entries(over)) if (v !== undefined && v !== "") (out as Record<string, unknown>)[k] = v
  return out
}

export const feedFetcher =
  (source: string, params: ListItemsParams) =>
  (cursor: string | null): Promise<MarketItemPage> =>
    marketplace.listItems(source, { ...params, cursor })

/** Stable key for `useInfinite`: any change restarts the feed. */
export const feedKey = (source: string, params: ListItemsParams) => JSON.stringify([source, { ...params, cursor: undefined }])
