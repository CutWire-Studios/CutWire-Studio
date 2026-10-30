import type { MarketItemPage, StockFilter } from "@cutwire/api-client"
import { Button, EmptyState, Input, Select, Spinner, Switch, cn } from "@cutwire/ui"
import { PackageOpen, X } from "lucide-react"
import { useEffect, useMemo, useState } from "react"
import { useSearchParams } from "react-router-dom"
import { useInfinite } from "../community/data"
import { ItemGrid, QuotaBadge } from "./cards"
import { useMarketplace } from "./manifest"
import { feedFetcher, feedKey, feedParams } from "./lib/items"
import type { StockBrowserSection } from "./lib/layout"
import { parseSearch, patchSearch, toSearchParams, type SearchState } from "./lib/url-state"
import { ResolveDialog } from "./resolve-dialog"

const ANY = "__any"

function FilterField({ filter, value, onChange }: { filter: StockFilter; value: string; onChange: (v: string) => void }) {
  const [text, setText] = useState(value)
  useEffect(() => setText(value), [value])
  return (
    <div className="space-y-1.5">
      <h2 className="text-label-md">{filter.label}</h2>
      {filter.kind === "select" && (
        <Select
          value={value || ANY}
          onValueChange={(v) => onChange(v === ANY ? "" : v)}
          options={[{ value: ANY, label: "Any" }, ...(filter.options ?? []).map((o) => ({ value: o.id, label: o.label }))]}
        />
      )}
      {filter.kind === "toggle" && <Switch checked={value === "1"} onCheckedChange={(c) => onChange(c ? "1" : "")} aria-label={filter.label} />}
      {filter.kind === "range" && (
        <form
          onSubmit={(e) => {
            e.preventDefault()
            onChange(text.trim())
          }}
        >
          <Input value={text} onChange={(e) => setText(e.target.value)} onBlur={() => onChange(text.trim())} placeholder="min-max" aria-label={filter.label} />
        </form>
      )}
    </div>
  )
}

export const ALL_SOURCES = "*"

export function StockBrowser({ section }: { section: StockBrowserSection }) {
  const { source: lookup, feature } = useMarketplace()
  const sourceId = section.source.id
  const source = lookup(sourceId)
  const providers = source?.providers ?? []
  const [params, setParams] = useSearchParams()
  const state = useMemo(() => parseSearch(params), [params])
  const fanOut = providers.length > 1
  const selected = state.provider || section.source.provider || (fanOut ? ALL_SOURCES : (providers[0]?.id ?? ""))
  const provider = providers.find((p) => p.id === selected)
  const [q, setQ] = useState(state.q)
  useEffect(() => setQ(state.q), [state.q])

  const [page, setPage] = useState<Pick<MarketItemPage, "quota" | "warnings">>({ quota: null })
  const update = (patch: Partial<SearchState>) => setParams(toSearchParams(patchSearch({ ...state, provider: selected }, patch)))

  const filters = Object.fromEntries(Object.entries(state.filters).filter(([k]) => provider?.filters.some((f) => f.id === k)))
  const query = feedParams(section.source, { provider: selected, q: state.q, filters })
  const feed = useInfinite(
    (cursor) =>
      feedFetcher(sourceId, query)(cursor).then((p) => {
        setPage(p)
        return p
      }),
    feedKey(sourceId, query),
  )
  const filtered = !!(state.q || Object.keys(state.filters).length)
  const canResolve = !!source?.canResolve && feature("resolve")
  const skipped = page.warnings?.map((w) => w.split(":")[0]).join(", ")

  return (
    <div className="grid gap-8 lg:grid-cols-[16rem_1fr]">
      <aside className="space-y-6 lg:sticky lg:top-24 lg:self-start" aria-label="Filters">
        <form
          role="search"
          onSubmit={(e) => {
            e.preventDefault()
            update({ q })
          }}
        >
          <Input value={q} onChange={(e) => setQ(e.target.value)} placeholder="Search" aria-label={`Search ${source?.label ?? ""}`} />
        </form>
        {provider?.filters.map((f) => (
          <FilterField key={f.id} filter={f} value={state.filters[f.id] ?? ""} onChange={(v) => update({ filters: { ...state.filters, [f.id]: v } })} />
        ))}
        {filtered && (
          <Button variant="ghost" size="sm" onClick={() => update({ q: "", filters: {} })}>
            <X className="size-4" aria-hidden /> Clear filters
          </Button>
        )}
        {canResolve && <ResolveDialog />}
      </aside>

      <section aria-live="polite" className="min-w-0 space-y-4">
        <div className="flex flex-wrap items-center gap-2">
          <h2 className="text-headline-md">{state.q ? `Results for "${state.q}"` : (source?.label ?? "Search")}</h2>
          <QuotaBadge quota={provider ? (page.quota ?? provider.quota) : null} />
        </div>
        {fanOut && (
          <div role="group" aria-label="Sources" className="flex flex-wrap gap-1.5">
            {[{ id: ALL_SOURCES, label: "All sources" }, ...providers].map((p) => (
              <button
                key={p.id}
                type="button"
                aria-pressed={p.id === selected}
                onClick={() => update({ provider: p.id })}
                className={cn(
                  "text-label-md cursor-pointer rounded-full border px-4 py-1.5 transition-colors",
                  p.id === selected ? "border-primary bg-primary text-primary-foreground" : "border-border hover:bg-muted",
                )}
              >
                {p.label}
              </button>
            ))}
          </div>
        )}
        {skipped && <p className="text-sm text-muted-foreground">Some sources did not respond: {skipped}.</p>}
        {feed.error && (
          <EmptyState title="Could not load results" description={feed.error.message} action={<Button onClick={feed.reload}>Try again</Button>} />
        )}
        {!feed.error && !feed.loading && feed.items.length === 0 && (
          <EmptyState icon={<PackageOpen className="size-6" aria-hidden />} title="Nothing found" description="Try a broader search or remove some filters." />
        )}
        {(feed.items.length > 0 || feed.loading) && !feed.error && <ItemGrid items={feed.items} loading={feed.loading} />}
        <div ref={feed.sentinel} className="flex h-10 justify-center">
          {feed.loading && feed.items.length > 0 && <Spinner />}
        </div>
      </section>
    </div>
  )
}
