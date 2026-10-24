import { catalog, getPath, groupsIn, tokensIn, type Category, type JsonObject, type TokenEntry } from "@cutwire/drift-theme"
import { Input, Switch } from "@cutwire/ui"
import { ChevronRight, Search } from "lucide-react"
import { useEffect, useMemo } from "react"
import { leafName, matchesQuery, modifiedPaths } from "@/lib/tokens"
import { useComputed } from "@/state/computed"
import { useEditor } from "@/state/editor"
import { useUi } from "@/state/ui"
import { ValuePreview } from "../inspector/refPicker"

export const SEARCH_ID = "token-search"
const CATEGORY_LABEL: Record<Category, string> = {
  palette: "Palette",
  semantic: "Semantic",
  components: "Components",
  metrics: "Metrics",
  typography: "Type",
  motion: "Motion",
  window: "Window",
}

function TokenRow({ token, selected, modified, resolved, label, onSelect }: { token: TokenEntry; selected: boolean; modified: boolean; resolved: JsonObject; label: string; onSelect(): void }) {
  return (
    <button
      type="button"
      data-path={token.path}
      onClick={onSelect}
      className={`flex w-full cursor-pointer items-center gap-2 rounded-md px-2 py-1 text-left text-xs hover:bg-muted ${selected ? "bg-muted font-semibold ring-1 ring-primary" : ""}`}
    >
      <ValuePreview token={token} value={getPath(resolved, token.path)} />
      <span className="min-w-0 flex-1 truncate font-mono" title={token.path}>
        {label}
      </span>
      {modified && <span className="size-1.5 shrink-0 rounded-full bg-primary" aria-label="modified" />}
    </button>
  )
}

function useRows() {
  const category = useUi((s) => s.category)
  const search = useUi((s) => s.search.trim())
  const modifiedOnly = useUi((s) => s.modifiedOnly)
  const doc = useEditor((s) => s.doc)
  const side = useEditor((s) => s.side)
  const modified = useMemo(() => modifiedPaths(doc, side), [doc, side])
  const results = useMemo(() => {
    if (!search) return null
    return catalog.tokens.filter((t) => (!modifiedOnly || modified.has(t.path)) && matchesQuery(t, search)).slice(0, 200)
  }, [search, modifiedOnly, modified])
  const groups = useMemo(() => {
    const byGroup = new Map<string, TokenEntry[]>()
    for (const t of tokensIn(category)) {
      if (modifiedOnly && !modified.has(t.path)) continue
      const list = byGroup.get(t.group)
      if (list) list.push(t)
      else byGroup.set(t.group, [t])
    }
    return groupsIn(category).filter((g) => byGroup.has(g.group)).map((g) => ({ ...g, tokens: byGroup.get(g.group) ?? [] }))
  }, [category, modifiedOnly, modified])
  const counts = useMemo(() => {
    const c: Partial<Record<Category, number>> = {}
    for (const p of modified) {
      const k = p.split(".")[0] as Category
      c[k] = (c[k] ?? 0) + 1
    }
    return c
  }, [modified])
  return { results, groups, modified, counts }
}

export function Navigator() {
  const { results, groups, modified, counts } = useRows()
  const { resolved } = useComputed()
  const { category, search, modifiedOnly, expanded, selected } = useUi()
  const ui = useUi.getState()

  useEffect(() => {
    if (!selected) return
    document.querySelector(`[data-path="${CSS.escape(selected)}"]`)?.scrollIntoView({ block: "nearest" })
  }, [selected, expanded])

  const row = (t: TokenEntry, label: string) => (
    <TokenRow key={t.path} token={t} selected={selected === t.path} modified={modified.has(t.path)} resolved={resolved.document} label={label} onSelect={() => ui.select(t.path, t.group)} />
  )

  return (
    <div className="flex h-full min-h-0 flex-col">
      <div className="flex flex-col gap-2 border-b border-border p-3">
        <div className="relative">
          <Search className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-muted-foreground" aria-hidden />
          <Input id={SEARCH_ID} aria-label="Search tokens" placeholder="Search tokens and groups  ( / )" className="pl-9" value={search} onChange={(e) => ui.setSearch(e.target.value)} />
        </div>
        <div className="flex flex-wrap gap-1" role="tablist" aria-label="Token tier">
          {catalog.categories.map((c) => (
            <button
              key={c}
              type="button"
              role="tab"
              aria-selected={category === c && !search}
              onClick={() => {
                ui.setSearch("")
                ui.setCategory(c)
              }}
              className={`cursor-pointer rounded-full px-2.5 py-1 text-xs font-medium ${category === c && !search ? "bg-primary text-primary-foreground" : "bg-muted text-muted-foreground hover:text-foreground"}`}
            >
              {CATEGORY_LABEL[c]}
              {counts[c] ? <span className="ml-1 opacity-80">{counts[c]}</span> : null}
            </button>
          ))}
        </div>
        <label className="flex items-center justify-between text-xs text-muted-foreground">
          Modified only
          <Switch checked={modifiedOnly} onCheckedChange={ui.setModifiedOnly} aria-label="Modified only" />
        </label>
      </div>
      <div className="min-h-0 flex-1 overflow-y-auto p-2">
        {results ? (
          <>
            {results.map((t) => row(t, t.path))}
            {results.length === 0 && <p className="p-3 text-sm text-muted-foreground">No tokens match.</p>}
            {results.length === 200 && <p className="p-2 text-[11px] text-muted-foreground">First 200 matches. Refine the search.</p>}
          </>
        ) : (
          groups.map((g) => {
            const key = `${g.category}/${g.group}`
            const open = !!expanded[key] || (modifiedOnly && g.tokens.length < 30)
            return (
              <div key={key}>
                <button type="button" aria-expanded={open} onClick={() => ui.toggleGroup(key)} className="flex w-full cursor-pointer items-center gap-1.5 rounded-md px-2 py-1.5 text-left text-sm font-medium hover:bg-muted">
                  <ChevronRight className={`size-3.5 text-muted-foreground transition-transform ${open ? "rotate-90" : ""}`} aria-hidden />
                  <span className="min-w-0 flex-1 truncate">{g.group}</span>
                  <span className="text-xs text-muted-foreground">{g.tokens.length}</span>
                </button>
                {open && <div className="ml-3 border-l border-border pl-1">{g.tokens.map((t) => row(t, leafName(t.path, t)))}</div>}
              </div>
            )
          })
        )}
        {!results && groups.length === 0 && <p className="p-3 text-sm text-muted-foreground">{modifiedOnly ? "Nothing modified in this tier yet." : "No tokens."}</p>}
      </div>
    </div>
  )
}
