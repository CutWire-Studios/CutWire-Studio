import { community, type ItemCard as Card, type KindInfo, type ListParams } from "@cutwire/api-client"
import { Button, Carousel, Input, Skeleton } from "@cutwire/ui"
import { Search } from "lucide-react"
import { useState, type FormEvent } from "react"
import { Link, useNavigate } from "react-router-dom"
import { ItemCard, CardSkeleton } from "./cards"
import { useAsync, useKinds } from "./data"
import { browseHref } from "./lib/url-state"

function Row({ title, params, seeAll, kinds }: { title: string; params: ListParams; seeAll: string; kinds?: KindInfo[] }) {
  const { data, loading, error } = useAsync(() => community.list({ ...params, limit: 12 }), [JSON.stringify(params)])
  if (error || (!loading && !data?.items.length)) return null
  const items: Card[] = data?.items ?? []
  return (
    <Carousel
      title={title}
      slideClassName="w-60 sm:w-64"
      action={
        <Button asChild variant="ghost" size="sm">
          <Link to={seeAll}>See all</Link>
        </Button>
      }
    >
      {loading && !data ? [0, 1, 2, 3].map((i) => <CardSkeleton key={i} />) : items.map((i) => <ItemCard key={i.id} item={i} kinds={kinds} />)}
    </Carousel>
  )
}

export function CommunityHome() {
  const navigate = useNavigate()
  const [q, setQ] = useState("")
  const { data: kinds, loading } = useKinds()
  const enabled = kinds ?? []

  const submit = (e: FormEvent) => {
    e.preventDefault()
    navigate(browseHref({ q }))
  }

  return (
    <div className="space-y-12">
      <section className="space-y-5 pt-2">
        <div className="space-y-2">
          <p className="text-eyebrow text-primary">Community</p>
          <h1 className="text-headline-lg">Effects, transitions, themes and more, made by people who edit.</h1>
        </div>
        <form onSubmit={submit} role="search" className="flex max-w-2xl gap-2">
          <div className="relative flex-1">
            <Search className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-muted-foreground" aria-hidden />
            <Input value={q} onChange={(e) => setQ(e.target.value)} placeholder="Search the community" aria-label="Search the community" className="pl-9" />
          </div>
          <Button type="submit">Search</Button>
        </form>
        <div className="flex flex-wrap gap-2" aria-label="Kinds">
          {loading && !kinds && <Skeleton className="h-8 w-64 rounded-full" />}
          {enabled.map((k) => (
            <Link key={k.id} to={browseHref({ kind: k.id })} className="text-label-md rounded-full border border-border px-4 py-1.5 transition-colors hover:bg-muted">
              {k.displayName}
            </Link>
          ))}
        </div>
      </section>

      <Row title="Trending" params={{ sort: "trending" }} seeAll={browseHref({ sort: "trending" })} kinds={enabled} />
      <Row title="Top this month" params={{ sort: "top", period: "month" }} seeAll={browseHref({ sort: "top" })} kinds={enabled} />
      <Row title="New" params={{ sort: "new" }} seeAll={browseHref({ sort: "new" })} kinds={enabled} />
      {enabled.map((k) => (
        <Row key={k.id} title={k.displayName} params={{ kind: k.id, sort: "trending" }} seeAll={browseHref({ kind: k.id })} kinds={enabled} />
      ))}
    </div>
  )
}
