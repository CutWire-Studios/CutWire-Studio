import type { MarketCategory } from "@cutwire/api-client"
import { cn } from "@cutwire/ui"
import { useMarketplace } from "./manifest"
import { usePageState } from "./page-state"
import type { ChipsSection } from "./lib/layout"

function ChipRow({ label, options, value, onChange }: { label: string; options: { id: string; label: string }[]; value: string; onChange: (id: string) => void }) {
  return (
    <div role="group" aria-label={label} className="flex flex-wrap gap-1.5">
      {options.map((c) => (
        <button
          key={c.id}
          type="button"
          aria-pressed={value === c.id}
          onClick={() => onChange(c.id)}
          className={cn(
            "text-label-md cursor-pointer rounded-full border px-4 py-1.5 transition-colors",
            value === c.id ? "border-primary bg-primary text-primary-foreground" : "border-border hover:bg-muted",
          )}
        >
          {c.label}
        </button>
      ))}
    </div>
  )
}

/** Category ids for children are `parent/child`, so the value alone says which row to open. */
export function CategoryChips({ categories, value, onChange }: { categories: MarketCategory[]; value: string; onChange: (id: string) => void }) {
  const parentId = value.split("/")[0]!
  const children = categories.find((c) => c.id === parentId)?.children ?? []
  return (
    <div className="space-y-2">
      <ChipRow label="Categories" options={[{ id: "", label: "All" }, ...categories]} value={parentId} onChange={onChange} />
      {children.length > 0 && <ChipRow label="Subcategories" options={[{ id: parentId, label: "All" }, ...children]} value={value} onChange={onChange} />}
    </div>
  )
}

export function Chips({ section }: { section: ChipsSection }) {
  const { source } = useMarketplace()
  const { categories, setCategory } = usePageState()
  const list = source(section.source.id)?.categories ?? []
  if (list.length === 0) return null
  return <CategoryChips categories={list} value={categories[section.target] ?? section.source.category ?? ""} onChange={(id) => setCategory(section.target, id)} />
}
