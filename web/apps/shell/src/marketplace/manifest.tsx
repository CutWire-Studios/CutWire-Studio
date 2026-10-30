import { marketplace, type MarketManifest, type MarketSource } from "@cutwire/api-client"
import { Button, EmptyState, Skeleton } from "@cutwire/ui"
import { AlertTriangle } from "lucide-react"
import { createContext, useContext, useMemo, type ReactNode } from "react"
import { useAsync } from "../community/data"
import { pruneLayout, type Page } from "./lib/layout"

interface MarketplaceContext {
  manifest: MarketManifest
  pages: Page[]
  source: (id: string) => MarketSource | undefined
  /** Features default to on; only an explicit `false` turns one off. */
  feature: (key: string) => boolean
}

const Ctx = createContext<MarketplaceContext | null>(null)

let cached: { manifest: MarketManifest; etag: string | null } | null = null

async function loadManifest(): Promise<MarketManifest> {
  const res = await marketplace.getManifest(cached?.etag)
  if (res.notModified && cached) return cached.manifest
  if (!res.notModified) cached = { manifest: res.manifest, etag: res.etag }
  return cached!.manifest
}

export function MarketplaceProvider({ children }: { children: ReactNode }) {
  const { data, error, loading, reload } = useAsync(loadManifest, [])
  const value = useMemo<MarketplaceContext | null>(
    () =>
      data
        ? {
            manifest: data,
            pages: pruneLayout(data.pages, data.sources),
            source: (id) => data.sources[id],
            feature: (key) => data.features[key] !== false,
          }
        : null,
    [data],
  )
  if (error && !value) {
    return (
      <EmptyState
        icon={<AlertTriangle className="size-6" aria-hidden />}
        title="Could not load the marketplace"
        description={error.message}
        action={<Button onClick={reload}>Try again</Button>}
      />
    )
  }
  if (loading && !value) return <Skeleton className="h-64 w-full rounded-xl" />
  return value && <Ctx.Provider value={value}>{children}</Ctx.Provider>
}

export function useMarketplace(): MarketplaceContext {
  const v = useContext(Ctx)
  if (!v) throw new Error("useMarketplace needs a MarketplaceProvider")
  return v
}
