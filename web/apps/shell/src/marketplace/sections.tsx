import { marketplace, type MarketItem } from "@cutwire/api-client"
import { Badge, Button, EmptyState, Input, Spinner, cn } from "@cutwire/ui"
import { ArrowRight, Info, TriangleAlert, Trash2, Search } from "lucide-react"
import { useState, type ComponentType } from "react"
import { Link } from "react-router-dom"
import { useAsync, useInfinite } from "../community/data"
import { ActionButton } from "./actions"
import { ItemCard, ItemGrid, Thumb } from "./cards"
import { Chips } from "./chips"
import { clearFinished, useDownloads } from "./lib/downloads"
import { feedFetcher, feedKey, feedParams } from "./lib/items"
import type { GridSection, HeroSection, NoticeSection, Section, SectionType, ShelfSection } from "./lib/layout"
import { pagePath } from "./lib/layout"
import { SfxBrowser } from "./sfx-browser"
import { StockBrowser } from "./stock-browser"
import { usePageState } from "./page-state"

function Hero({ section }: { section: HeroSection }) {
  const action = section.action
  return (
    <section aria-label={section.title} className="panel relative overflow-hidden p-8 sm:p-12">
      {section.image && <img src={section.image} alt="" className="absolute inset-0 size-full object-cover opacity-30" />}
      <div className="relative max-w-2xl space-y-3">
        <h2 className="text-headline-lg">{section.title}</h2>
        {section.subtitle && <p className="text-muted-foreground">{section.subtitle}</p>}
        {action && <ActionButton action={action}>{action.type === "open-url" ? "Learn more" : "Explore"}</ActionButton>}
      </div>
    </section>
  )
}

function Notice({ section }: { section: NoticeSection }) {
  const warning = section.tone === "warning"
  const Icon = warning ? TriangleAlert : Info
  return (
    <div role={warning ? "alert" : "note"} className={cn("flex items-start gap-3 rounded-xl border p-4 text-sm", warning ? "border-destructive/40 bg-destructive/10" : "border-border bg-muted")}>
      <Icon className="mt-0.5 size-4 shrink-0" aria-hidden />
      <p>{section.text}</p>
    </div>
  )
}

function Shelf({ section }: { section: ShelfSection }) {
  const params = feedParams(section.source, { limit: section.limit ?? 12 })
  const { data, loading, error, reload } = useAsync(() => marketplace.listItems(section.source.id, params), [feedKey(section.source.id, params)])
  if (!loading && !error && data?.items.length === 0) return null
  return (
    <section aria-labelledby={`shelf-${section.id}`} className="space-y-3">
      <div className="flex items-center justify-between gap-3">
        <h2 id={`shelf-${section.id}`} className="text-headline-md">
          {section.title}
        </h2>
        {section.seeAll && (
          <Button asChild variant="ghost" size="sm">
            <Link to={pagePath(section.seeAll.page)}>
              See all <ArrowRight className="size-4" aria-hidden />
            </Link>
          </Button>
        )}
      </div>
      {error ? (
        <p className="text-sm text-muted-foreground">
          Could not load this shelf.{" "}
          <button type="button" onClick={reload} className="cursor-pointer text-primary underline underline-offset-2">
            Try again
          </button>
        </p>
      ) : (
        <ul tabIndex={0} aria-label={section.title} className="-mx-1 flex snap-x gap-4 overflow-x-auto px-1 pb-3 focus-visible:outline-2 focus-visible:outline-ring">
          {(data?.items ?? []).map((i: MarketItem) => (
            <li key={i.id} className="w-60 shrink-0 snap-start">
              <ItemCard item={i} />
            </li>
          ))}
          {loading && !data && Array.from({ length: 5 }, (_, i) => <li key={i} className="h-52 w-60 shrink-0 animate-pulse rounded-xl bg-muted" aria-hidden />)}
        </ul>
      )}
    </section>
  )
}

function Grid({ section }: { section: GridSection }) {
  const { categories } = usePageState()
  const [q, setQ] = useState("")
  const [submitted, setSubmitted] = useState("")
  const query = feedParams(section.source, { q: submitted, category: categories[section.id] })
  const feed = useInfinite(feedFetcher(section.source.id, query), feedKey(section.source.id, query))
  return (
    <section aria-label={section.title ?? "Items"} className="space-y-4">
      {section.title && <h2 className="text-headline-md">{section.title}</h2>}
      {section.searchable && (
        <form
          role="search"
          className="relative max-w-md"
          onSubmit={(e) => {
            e.preventDefault()
            setSubmitted(q.trim())
          }}
        >
          <Search className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-muted-foreground" aria-hidden />
          <Input value={q} onChange={(e) => setQ(e.target.value)} placeholder="Search" aria-label="Search" className="pl-9" />
        </form>
      )}
      <div aria-live="polite" className="space-y-4">
        {feed.error && <EmptyState title="Could not load items" description={feed.error.message} action={<Button onClick={feed.reload}>Try again</Button>} />}
        {!feed.error && !feed.loading && feed.items.length === 0 && <EmptyState title="Nothing here yet" description="Try a different search or category." />}
        {(feed.items.length > 0 || feed.loading) && !feed.error && <ItemGrid items={feed.items} loading={feed.loading} />}
        <div ref={feed.sentinel} className="flex h-10 justify-center">
          {feed.loading && feed.items.length > 0 ? <Spinner /> : feed.hasMore && !feed.error && <Button variant="secondary" size="sm" onClick={feed.loadMore}>Load more</Button>}
        </div>
      </div>
    </section>
  )
}

export function Downloads() {
  const entries = useDownloads()
  const finished = entries.some((e) => e.status === "ready" || e.status === "failed")
  return (
    <section aria-label="Downloads" className="space-y-4">
      <div className="flex items-center justify-between gap-3">
        <h2 className="text-headline-md">Downloads</h2>
        {finished && (
          <Button variant="ghost" size="sm" onClick={clearFinished}>
            <Trash2 className="size-4" aria-hidden /> Clear finished
          </Button>
        )}
      </div>
      {entries.length === 0 ? (
        <EmptyState title="No downloads yet" description="Items you download in this session show up here." />
      ) : (
        <ul className="grid gap-2" aria-live="polite">
          {entries.map((e) => {
            const pct = Math.round(e.progress * 100)
            const active = e.status === "starting" || e.status === "queued" || e.status === "processing"
            return (
              <li key={e.id} className="panel flex items-center gap-3 p-3">
                <Thumb src={e.thumbUrl || null} alt="" className="w-24 shrink-0" />
                <div className="min-w-0 flex-1 space-y-1">
                  <p className="truncate font-medium">{e.title}</p>
                  {active && (
                    <div role="progressbar" aria-label={`Preparing ${e.title}`} aria-valuenow={pct} aria-valuemin={0} aria-valuemax={100} className="h-1.5 overflow-hidden rounded-full bg-muted">
                      <div className="h-full bg-primary transition-[width]" style={{ width: `${pct}%` }} />
                    </div>
                  )}
                  {e.status === "failed" && <p className="text-sm text-destructive">{e.error}</p>}
                  {e.status === "ready" && (
                    <p className="flex flex-wrap gap-x-3 text-sm text-muted-foreground">
                      {e.files.map((f) => (
                        <a key={f.url} href={f.url} download={f.name} className="text-primary underline underline-offset-2">
                          Download {f.name} again
                        </a>
                      ))}
                    </p>
                  )}
                </div>
                <Badge tone={e.status === "ready" ? "primary" : "neutral"}>{active ? (e.status === "processing" ? `${pct}%` : "Waiting") : e.status === "ready" ? "Ready" : "Failed"}</Badge>
              </li>
            )
          })}
        </ul>
      )}
    </section>
  )
}

export const sectionComponents: { [T in SectionType]: ComponentType<{ section: Extract<Section, { type: T }> }> } = {
  hero: Hero,
  notice: Notice,
  shelf: Shelf,
  grid: Grid,
  chips: Chips,
  "stock-browser": StockBrowser,
  "sfx-browser": SfxBrowser,
  downloads: Downloads,
}
