import { community } from "@cutwire/api-client"
import { Badge, Button, EmptyState, Input, Select, Spinner } from "@cutwire/ui"
import { PackageOpen, X } from "lucide-react"
import { useEffect, useMemo, useState } from "react"
import { useSearchParams } from "react-router-dom"
import { ItemGrid } from "./cards"
import { useInfinite, useKinds } from "./data"
import { SORTS, parseBrowse, patchBrowse, toBrowseParams, type BrowseState } from "./lib/url-state"

const SORT_LABELS: Record<string, string> = { trending: "Trending", top: "Top rated", new: "Newest", downloads: "Most downloaded" }
const ANY = "__any"

export function BrowsePage() {
  const [params, setParams] = useSearchParams()
  const state = useMemo(() => parseBrowse(params), [params])
  const { data: kinds } = useKinds()
  const kind = kinds?.find((k) => k.id === state.kind)
  const [q, setQ] = useState(state.q)
  useEffect(() => setQ(state.q), [state.q])

  const update = (patch: Partial<BrowseState>) => setParams(toBrowseParams(patchBrowse(state, patch)), { replace: false })

  const key = params.toString()
  const feed = useInfinite(
    (cursor) =>
      community.list({ kind: state.kind, q: state.q, tags: state.tags, category: state.category, sort: state.sort, cursor }),
    key,
  )

  const toggleTag = (t: string) => update({ tags: state.tags.includes(t) ? state.tags.filter((x) => x !== t) : [...state.tags, t] })
  const filtered = !!(state.kind || state.q || state.tags.length || state.category)

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

        <Field label="Sort">
          <Select
            value={state.sort}
            onValueChange={(v) => update({ sort: v as BrowseState["sort"] })}
            options={SORTS.map((s) => ({ value: s, label: SORT_LABELS[s] }))}
          />
        </Field>

        <Field label="Kind">
          <Select
            value={state.kind || ANY}
            onValueChange={(v) => update({ kind: v === ANY ? "" : v })}
            options={[{ value: ANY, label: "All kinds" }, ...(kinds ?? []).map((k) => ({ value: k.id, label: k.displayName }))]}
          />
        </Field>

        {kind && (
          <>
            <Field label="Category">
              <Select
                value={state.category || ANY}
                onValueChange={(v) => update({ category: v === ANY ? "" : v })}
                options={[{ value: ANY, label: "Any category" }, ...kind.categories.map((c) => ({ value: c, label: c }))]}
              />
            </Field>
            <Field label="Tags">
              <div className="flex flex-wrap gap-1.5">
                {kind.tags.map((t) => {
                  const on = state.tags.includes(t)
                  return (
                    <button
                      key={t}
                      type="button"
                      aria-pressed={on}
                      onClick={() => toggleTag(t)}
                      className={`text-label-md cursor-pointer rounded-full border px-3 py-1 transition-colors ${on ? "border-primary bg-primary text-primary-foreground" : "border-border hover:bg-muted"}`}
                    >
                      {t}
                    </button>
                  )
                })}
              </div>
            </Field>
          </>
        )}
        {filtered && (
          <Button variant="ghost" size="sm" onClick={() => setParams(new URLSearchParams())}>
            <X className="size-4" aria-hidden /> Clear filters
          </Button>
        )}
      </aside>

      <section aria-live="polite" className="min-w-0 space-y-4">
        <div className="flex flex-wrap items-center gap-2">
          <h1 className="text-headline-md">{state.q ? `Results for "${state.q}"` : kind ? kind.displayName : "Browse"}</h1>
          {state.tags.map((t) => (
            <Badge key={t}>{t}</Badge>
          ))}
        </div>
        {feed.error && (
          <EmptyState title="Could not load items" description={feed.error.message} action={<Button onClick={feed.reload}>Try again</Button>} />
        )}
        {!feed.error && !feed.loading && feed.items.length === 0 && (
          <EmptyState
            icon={<PackageOpen className="size-6" aria-hidden />}
            title={filtered ? "Nothing matches these filters" : "Nothing here yet"}
            description={filtered ? "Try removing a tag or searching for something broader." : "Be the first to share something."}
            action={
              filtered ? (
                <Button variant="secondary" onClick={() => setParams(new URLSearchParams())}>
                  Clear filters
                </Button>
              ) : undefined
            }
          />
        )}
        {(feed.items.length > 0 || feed.loading) && !feed.error && <ItemGrid items={feed.items} kinds={kinds} loading={feed.loading} />}
        <div ref={feed.sentinel} className="flex h-10 justify-center">
          {feed.loading && feed.items.length > 0 && <Spinner />}
        </div>
      </section>
    </div>
  )
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="space-y-1.5">
      <h2 className="text-label-md">{label}</h2>
      {children}
    </div>
  )
}
