import type { ItemCard as Card, KindInfo } from "@cutwire/api-client"
import { num } from "@cutwire/api-client"
import { Avatar, Badge, Skeleton, cn } from "@cutwire/ui"
import { ArrowDown, ArrowUp, Download, ImageOff } from "lucide-react"
import { useEffect, useRef, useState } from "react"
import { Link } from "react-router-dom"
import { kindName } from "./data"
import { compact } from "./lib/text"

const STRIP_INTERVAL_MS = 90

/** Animated scrub through a horizontal preview strip (square-ish frames side by side), cover-fitted to the box. */
export function StripPreview({ src, active }: { src: string; active: boolean }) {
  const [dims, setDims] = useState<{ frames: number; ratio: number } | null>(null)
  const [frame, setFrame] = useState(0)
  const raf = useRef<number | null>(null)

  useEffect(() => {
    if (!active || dims) return
    const img = new Image()
    img.onload = () => {
      const frames = Math.max(1, Math.round(img.naturalWidth / img.naturalHeight))
      setDims({ frames, ratio: img.naturalWidth / frames / img.naturalHeight })
    }
    img.src = src
  }, [active, dims, src])

  useEffect(() => {
    if (!active || !dims || dims.frames < 2) {
      setFrame(0)
      return
    }
    const reduce = window.matchMedia?.("(prefers-reduced-motion: reduce)").matches
    if (reduce) return
    let last = 0
    const tick = (t: number) => {
      if (t - last >= STRIP_INTERVAL_MS) {
        last = t
        setFrame((f) => (f + 1) % dims.frames)
      }
      raf.current = requestAnimationFrame(tick)
    }
    raf.current = requestAnimationFrame(tick)
    return () => {
      if (raf.current !== null) cancelAnimationFrame(raf.current)
    }
  }, [active, dims])

  if (!active || !dims) return null
  return (
    <div aria-hidden className="absolute inset-0 flex items-center overflow-hidden bg-black">
      <div className="relative w-full overflow-hidden" style={{ aspectRatio: String(dims.ratio) }}>
        <img
          src={src}
          alt=""
          className="block h-full max-w-none"
          style={{ width: `${dims.frames * 100}%`, transform: `translateX(-${(frame * 100) / dims.frames}%)` }}
        />
      </div>
    </div>
  )
}

export function Thumb({ src, alt, strip, hover, className }: { src: string | null; alt: string; strip?: string | null; hover?: boolean; className?: string }) {
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
      {strip && <StripPreview src={strip} active={!!hover} />}
    </div>
  )
}

export function KindBadge({ kind, kinds }: { kind: string; kinds?: KindInfo[] }) {
  return <Badge tone="primary">{kindName(kinds, kind)}</Badge>
}

export function ItemCard({ item, kinds, extra }: { item: Card; kinds?: KindInfo[]; extra?: React.ReactNode }) {
  const [hover, setHover] = useState(false)
  const score = num(item.score)
  return (
    <article
      className="group panel panel-hover relative flex h-full flex-col gap-3 p-3"
      onPointerEnter={() => setHover(true)}
      onPointerLeave={() => setHover(false)}
      onFocus={() => setHover(true)}
      onBlur={() => setHover(false)}
    >
      <Link to={`/community/${item.kind}/${item.slug}`} className="absolute inset-0 z-10 rounded-[inherit]" aria-label={item.title} />
      <div className="relative">
        <Thumb src={item.thumbnailUrl} alt="" strip={item.kind === "transition" ? item.previewStripUrl : null} hover={hover} />
        <div className="absolute top-2 left-2">
          <KindBadge kind={item.kind} kinds={kinds} />
        </div>
      </div>
      <div className="min-w-0 flex-1 space-y-1">
        <h3 className="truncate font-semibold" title={item.title}>
          {item.title}
        </h3>
        <p className="flex items-center gap-1.5 truncate text-sm text-muted-foreground">
          <Avatar src={item.owner.avatarUrl ?? undefined} name={item.owner.displayName} className="size-5 text-[9px]" />
          {item.owner.displayName}
        </p>
      </div>
      <div className="flex items-center gap-4 text-sm text-muted-foreground">
        <span className="inline-flex items-center gap-1" title="Score">
          {score < 0 ? <ArrowDown className="size-3.5" aria-hidden /> : <ArrowUp className="size-3.5" aria-hidden />}
          {compact(score)}
        </span>
        <span className="inline-flex items-center gap-1" title="Downloads">
          <Download className="size-3.5" aria-hidden />
          {compact(num(item.downloads))}
        </span>
        {item.status !== "published" && <Badge className="ml-auto">{item.status}</Badge>}
        {extra}
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

export function ItemGrid({ items, kinds, loading, extra }: { items: Card[]; kinds?: KindInfo[]; loading?: boolean; extra?: (i: Card) => React.ReactNode }) {
  return (
    <ul className="grid gap-4 [grid-template-columns:repeat(auto-fill,minmax(15rem,1fr))]">
      {items.map((i) => (
        <li key={i.id}>
          <ItemCard item={i} kinds={kinds} extra={extra?.(i)} />
        </li>
      ))}
      {loading && Array.from({ length: items.length ? 3 : 8 }, (_, i) => <CardSkeleton key={`s${i}`} />).map((s, i) => <li key={`sk${i}`}>{s}</li>)}
    </ul>
  )
}
