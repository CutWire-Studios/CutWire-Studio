import type { MarketItem, StockQuota } from "@cutwire/api-client"
import { Badge, Skeleton, Spinner, cn } from "@cutwire/ui"
import {
  AudioLines,
  Box,
  Clapperboard,
  Gauge,
  ImageIcon,
  ImageOff,
  Music,
  Palette,
  Pause,
  Play,
  ScanFace,
  Shuffle,
  Sparkles,
  SwatchBook,
  type LucideIcon,
  Volume2,
  Wand2,
} from "lucide-react"
import { useState } from "react"
import { Link } from "react-router-dom"
import { sharedAudio, useSharedAudio } from "./hooks"
import { formatDuration } from "./lib/format"
import { creatorOf, isCommunity, providerOf } from "./lib/items"
import { itemPath, resolveLink } from "./lib/layout"

export interface KindMeta {
  label: string
  icon: LucideIcon
  /** Tailwind aspect class of the thumbnail. */
  aspect: string
  /** Whether the preview is a sound that can be played from the card. */
  sound?: boolean
}

const VIDEO = "aspect-video"
const SQUARE = "aspect-square"

export const kindMeta: Record<string, KindMeta> = {
  video: { label: "Video", icon: Clapperboard, aspect: VIDEO },
  image: { label: "Photo", icon: ImageIcon, aspect: VIDEO },
  audio: { label: "Audio", icon: Volume2, aspect: VIDEO, sound: true },
  music: { label: "Music", icon: Music, aspect: VIDEO, sound: true },
  lottie: { label: "Animation", icon: Sparkles, aspect: SQUARE },
  model3d: { label: "3D model", icon: Box, aspect: SQUARE },
  "face-prop": { label: "Face prop", icon: ScanFace, aspect: SQUARE },
  effect: { label: "Effect", icon: Wand2, aspect: VIDEO },
  transition: { label: "Transition", icon: Shuffle, aspect: VIDEO },
  "audio-effect": { label: "Audio effect", icon: AudioLines, aspect: VIDEO, sound: true },
  theme: { label: "Theme", icon: Palette, aspect: VIDEO },
  look: { label: "Look", icon: SwatchBook, aspect: VIDEO },
  "effect-template": { label: "Effect template", icon: Sparkles, aspect: VIDEO },
}

export const metaFor = (mediaKind: string): KindMeta =>
  kindMeta[mediaKind] ?? { label: mediaKind, icon: ImageIcon, aspect: VIDEO }

export function Thumb({ src, alt, className, icon: Icon }: { src: string | null; alt: string; className?: string; icon?: LucideIcon }) {
  const [failed, setFailed] = useState(false)
  const Fallback = Icon ?? ImageOff
  return (
    <div className={cn("relative aspect-video overflow-hidden rounded-xl bg-muted", className)}>
      {src && !failed ? (
        <img src={src} alt={alt} loading="lazy" decoding="async" onError={() => setFailed(true)} className="size-full object-cover" />
      ) : (
        <div className="flex size-full items-center justify-center text-muted-foreground" aria-hidden>
          <Fallback className="size-7" />
        </div>
      )}
    </div>
  )
}

/** Community items live on their own pages; everything else opens the detail page. */
export function itemHref(item: Pick<MarketItem, "source" | "id" | "webUrl">): string {
  if (item.source.startsWith("community.") && item.webUrl) {
    const target = resolveLink(item.webUrl, window.location.origin)
    if (target.kind === "internal") return target.path
  }
  return itemPath(item.source, item.id)
}

function PreviewButton({ item }: { item: MarketItem }) {
  const audio = useSharedAudio()
  const mine = audio.currentId === item.id
  const playing = mine && audio.phase === "playing"
  const loading = mine && audio.phase === "loading"
  return (
    <button
      type="button"
      aria-label={playing ? `Pause ${item.title}` : `Play ${item.title}`}
      onClick={() => sharedAudio.toggle(item.id, item.previewUrl!)}
      className="absolute top-1/2 left-1/2 z-20 flex size-11 -translate-x-1/2 -translate-y-1/2 cursor-pointer items-center justify-center rounded-full bg-black/70 text-white transition-transform hover:scale-105 focus-visible:ring-2 focus-visible:ring-ring"
    >
      {loading ? <Spinner /> : playing ? <Pause className="size-5" aria-hidden /> : <Play className="size-5" aria-hidden />}
    </button>
  )
}

export function ItemCard({ item, showKind }: { item: MarketItem; showKind?: boolean }) {
  const meta = metaFor(item.mediaKind)
  const subtitle = item.subtitle ?? creatorOf(item)?.name ?? providerOf(item)
  const playable = meta.sound && !!item.previewUrl
  return (
    <article className="group panel panel-hover relative flex h-full flex-col gap-3 p-3">
      <Link to={itemHref(item)} className="absolute inset-0 z-10 rounded-[inherit]" aria-label={item.title} />
      <div className="relative">
        <Thumb src={item.thumbUrl || null} alt="" icon={meta.icon} className={meta.aspect} />
        {showKind && (
          <div className="absolute top-2 left-2">
            <Badge tone="primary">{meta.label}</Badge>
          </div>
        )}
        {playable && <PreviewButton item={item} />}
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
        {subtitle && <p className="truncate text-sm text-muted-foreground">{subtitle}</p>}
        {isCommunity(item) && item.category && <p className="truncate text-xs text-muted-foreground">{item.category}</p>}
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

export function ItemGrid({ items, loading, showKind }: { items: MarketItem[]; loading?: boolean; showKind?: boolean }) {
  return (
    <ul className="grid gap-4 [grid-template-columns:repeat(auto-fill,minmax(15rem,1fr))]">
      {items.map((i) => (
        <li key={`${i.source}/${i.id}`}>
          <ItemCard item={i} showKind={showKind} />
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
