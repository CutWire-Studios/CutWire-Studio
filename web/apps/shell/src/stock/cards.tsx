import type { StockItem, StockQuota } from "@cutwire/api-client"
import { Badge, Skeleton, cn } from "@cutwire/ui"
import { Gauge, ImageOff, Music, Play } from "lucide-react"
import { useState } from "react"
import { Link } from "react-router-dom"
import { formatDuration } from "./lib/format"

export const itemHref = (id: string) => `/stock/item/${encodeURIComponent(id)}`

export function Thumb({ src, alt, className }: { src: string | null; alt: string; className?: string }) {
  const [failed, setFailed] = useState(false)
  return (
    <div className={cn("relative aspect-video overflow-hidden rounded-xl bg-muted", className)}>
      {src && !failed ? (
        <img src={src} alt={alt} loading="lazy" decoding="async" onError={() => setFailed(true)} className="size-full object-cover" />
      ) : (
        <div className="flex size-full items-center justify-center text-muted-foreground" aria-hidden>
          <ImageOff className="size-6" />
        </div>
      )}
    </div>
  )
}

export function StockCard({ item, typeLabel }: { item: StockItem; typeLabel?: string }) {
  const audioOnly = !item.thumbUrl
  return (
    <article className="group panel panel-hover relative flex h-full flex-col gap-3 p-3">
      <Link to={itemHref(item.id)} className="absolute inset-0 z-10 rounded-[inherit]" aria-label={item.title} />
      <div className="relative">
        {audioOnly ? (
          <div className="flex aspect-video items-center justify-center rounded-xl bg-muted text-muted-foreground" aria-hidden>
            <Music className="size-8" />
          </div>
        ) : (
          <Thumb src={item.thumbUrl} alt="" />
        )}
        {typeLabel && (
          <div className="absolute top-2 left-2">
            <Badge tone="primary">{typeLabel}</Badge>
          </div>
        )}
        {item.durationMs != null && (
          <span className="absolute right-2 bottom-2 inline-flex items-center gap-1 rounded-full bg-black/70 px-2 py-0.5 text-xs text-white">
            <Play className="size-3" aria-hidden />
            {formatDuration(item.durationMs)}
          </span>
        )}
      </div>
      <div className="min-w-0 flex-1 space-y-1">
        <h3 className="truncate font-semibold" title={item.title}>
          {item.title}
        </h3>
        <p className="truncate text-sm text-muted-foreground">{item.creator?.name ?? item.provider}</p>
      </div>
    </article>
  )
}

export function CardSkeleton() {
  return (
    <div className="panel space-y-3 p-3" aria-hidden>
      <Skeleton className="aspect-video w-full rounded-xl" />
      <Skeleton className="h-5 w-3/4" />
      <Skeleton className="h-4 w-1/2" />
    </div>
  )
}

export function StockGrid({ items, loading, typeLabel }: { items: StockItem[]; loading?: boolean; typeLabel?: (t: string) => string }) {
  return (
    <ul className="grid gap-4 [grid-template-columns:repeat(auto-fill,minmax(15rem,1fr))]">
      {items.map((i) => (
        <li key={i.id}>
          <StockCard item={i} typeLabel={typeLabel?.(i.type)} />
        </li>
      ))}
      {loading &&
        Array.from({ length: items.length ? 3 : 8 }, (_, i) => (
          <li key={`s${i}`}>
            <CardSkeleton />
          </li>
        ))}
    </ul>
  )
}

export function QuotaBadge({ quota }: { quota: StockQuota | null | undefined }) {
  if (!quota) return null
  const text = quota.remaining == null ? `${quota.limit} per ${quota.window}` : `${quota.remaining} of ${quota.limit} left this ${quota.window}`
  return (
    <Badge title="Provider usage limit">
      <Gauge className="mr-1 size-3.5" aria-hidden />
      {text}
    </Badge>
  )
}
