import { stock, type StockFilter, type StockProvider, type StockQuota } from "@cutwire/api-client"
import { Button, EmptyState, Input, Select, Spinner, Switch, cn } from "@cutwire/ui"
import { PackageOpen, X } from "lucide-react"
import { useEffect, useMemo, useState } from "react"
import { useSearchParams } from "react-router-dom"
import { useAsync, useInfinite } from "../community/data"
import { QuotaBadge, StockGrid } from "./cards"
import { loadCatalog } from "./hooks"
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

export function SearchPage() {
  const [params, setParams] = useSearchParams()
  const state = useMemo(() => parseSearch(params), [params])
  const { data: catalog } = useAsync(loadCatalog, [])
  const types = catalog?.types ?? []
  const type = types.find((t) => t.id === state.type) ?? types[0]
  const provider: StockProvider | undefined = type?.providers.find((p) => p.id === state.provider) ?? type?.providers[0]
  const [q, setQ] = useState(state.q)
  useEffect(() => setQ(state.q), [state.q])

  const [pageQuota, setPageQuota] = useState<StockQuota | null>(null)
  const update = (patch: Partial<SearchState>) => setParams(toSearchParams(patchSearch({ ...state, type: type?.id ?? state.type }, patch)))

  const key = `${type?.id}|${params.toString()}`
  const feed = useInfinite(
    (cursor) =>
      stock
        .search({
        type: type!.id,
        provider: state.provider || undefined,
        q: state.q,
        cursor,
        filters: Object.fromEntries(Object.entries(state.filters).filter(([k]) => provider?.filters.some((f) => f.id === k))),
        })
        .then((p) => {
          setPageQuota(p.quota)
          return p
        }),
    type ? key : "pending",
  )
  const quota = provider?.quota
  const filtered = !!(state.q || Object.keys(state.filters).length)

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
          <Input value={q} onChange={(e) => setQ(e.target.value)} placeholder="Search" aria-label="Search" />
        </form>
        <div className="space-y-1.5">
          <h2 className="text-label-md">Type</h2>
          <Select value={type?.id ?? ""} onValueChange={(v) => update({ type: v })} options={types.map((t) => ({ value: t.id, label: t.label }))} />
        </div>
        {provider?.filters.map((f) => (
          <FilterField key={f.id} filter={f} value={state.filters[f.id] ?? ""} onChange={(v) => update({ filters: { ...state.filters, [f.id]: v } })} />
        ))}
        {filtered && (
          <Button variant="ghost" size="sm" onClick={() => update({ q: "", filters: {} })}>
            <X className="size-4" aria-hidden /> Clear filters
          </Button>
        )}
        <ResolveDialog />
      </aside>

      <section aria-live="polite" className="min-w-0 space-y-4">
        <div className="flex flex-wrap items-center gap-2">
          <h1 className="text-headline-md">{state.q ? `Results for "${state.q}"` : (type?.label ?? "Search")}</h1>
          <QuotaBadge quota={pageQuota ?? quota} />
        </div>
        {type && type.providers.length > 1 && (
          <div role="tablist" aria-label="Providers" className="flex flex-wrap gap-1.5">
            {type.providers.map((p) => {
              const on = p.id === provider?.id
              return (
                <button
                  key={p.id}
                  role="tab"
                  type="button"
                  aria-selected={on}
                  onClick={() => update({ provider: p.id })}
                  className={cn("text-label-md cursor-pointer rounded-full border px-4 py-1.5 transition-colors", on ? "border-primary bg-primary text-primary-foreground" : "border-border hover:bg-muted")}
                >
                  {p.label}
                </button>
              )
            })}
          </div>
        )}
        {feed.error && (
          <EmptyState title="Could not load results" description={feed.error.message} action={<Button onClick={feed.reload}>Try again</Button>} />
        )}
        {!feed.error && !feed.loading && feed.items.length === 0 && (
          <EmptyState
            icon={<PackageOpen className="size-6" aria-hidden />}
            title="Nothing found"
            description="Try a broader search or remove some filters."
          />
        )}
        {(feed.items.length > 0 || feed.loading) && !feed.error && <StockGrid items={feed.items} loading={feed.loading} />}
        <div ref={feed.sentinel} className="flex h-10 justify-center">
          {feed.loading && feed.items.length > 0 && <Spinner />}
        </div>
      </section>
    </div>
  )
}
