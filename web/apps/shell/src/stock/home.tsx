import { stock, type StockType } from "@cutwire/api-client"
import { Button, Carousel, Input, Select, Skeleton } from "@cutwire/ui"
import { AudioLines, Search, Shapes } from "lucide-react"
import { useState, type FormEvent } from "react"
import { Link, useNavigate } from "react-router-dom"
import { useAsync } from "../community/data"
import { CardSkeleton, StockCard } from "./cards"
import { loadCatalog } from "./hooks"
import { DriftNote } from "./layout"
import { searchHref } from "./lib/url-state"
import { ResolveDialog } from "./resolve-dialog"

function TypeRow({ type }: { type: StockType }) {
  const { data, loading, error } = useAsync(() => stock.search({ type: type.id, limit: 12 }), [type.id])
  if (error || (!loading && !data?.items.length)) return null
  return (
    <Carousel
      title={type.label}
      slideClassName="w-60 sm:w-64"
      action={
        <Button asChild variant="ghost" size="sm">
          <Link to={searchHref({ type: type.id })}>See all</Link>
        </Button>
      }
    >
      {loading && !data ? [0, 1, 2, 3].map((i) => <CardSkeleton key={i} />) : (data?.items ?? []).map((i) => <StockCard key={i.id} item={i} />)}
    </Carousel>
  )
}

function EntryCard({ to, icon, title, text }: { to: string; icon: React.ReactNode; title: string; text: string }) {
  return (
    <Link to={to} className="panel panel-hover flex items-center gap-4 p-5">
      <span className="flex size-12 items-center justify-center rounded-xl bg-muted text-primary">{icon}</span>
      <span>
        <span className="block font-semibold">{title}</span>
        <span className="text-sm text-muted-foreground">{text}</span>
      </span>
    </Link>
  )
}

export function StockHome() {
  const navigate = useNavigate()
  const [q, setQ] = useState("")
  const [type, setType] = useState("")
  const { data: catalog, loading } = useAsync(loadCatalog, [])
  const types = catalog?.types ?? []

  const submit = (e: FormEvent) => {
    e.preventDefault()
    navigate(searchHref({ q, type: type || types[0]?.id }))
  }

  return (
    <div className="space-y-12">
      <section className="space-y-5 pt-2">
        <div className="space-y-2">
          <p className="text-eyebrow text-primary">Stock Library</p>
          <h1 className="text-headline-lg">Footage, photos, music and sound, ready for your edit.</h1>
          <DriftNote />
        </div>
        <form onSubmit={submit} role="search" className="flex max-w-3xl flex-wrap gap-2">
          <Select
            className="w-40"
            value={type || types[0]?.id || ""}
            onValueChange={setType}
            options={types.map((t) => ({ value: t.id, label: t.label }))}
            aria-label="Type"
          />
          <div className="relative min-w-48 flex-1">
            <Search className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-muted-foreground" aria-hidden />
            <Input value={q} onChange={(e) => setQ(e.target.value)} placeholder="Search the library" aria-label="Search the library" className="pl-9" />
          </div>
          <Button type="submit">Search</Button>
          <ResolveDialog />
        </form>
        {loading && !catalog && <Skeleton className="h-8 w-64 rounded-full" />}
      </section>

      <div className="grid gap-4 sm:grid-cols-2">
        <EntryCard to="/stock/sfx" icon={<AudioLines className="size-6" aria-hidden />} title="Sound effects" text="Browse and download free sound effects." />
        <EntryCard to="/stock/drift-assets" icon={<Shapes className="size-6" aria-hidden />} title="Drift assets" text="Animations, face props and 3D models." />
      </div>

      {types.map((t) => (
        <TypeRow key={t.id} type={t} />
      ))}
    </div>
  )
}
