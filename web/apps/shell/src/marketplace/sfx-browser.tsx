import { Button, EmptyState, IconButton, Input, Spinner, cn } from "@cutwire/ui"
import type { MarketItem } from "@cutwire/api-client"
import { Download, Pause, Play, Search } from "lucide-react"
import { useMemo, useState } from "react"
import { useInfinite } from "../community/data"
import { CardSkeleton } from "./cards"
import { CategoryChips } from "./chips"
import { sharedAudio, useSharedAudio, useStopAudioOnLeave } from "./hooks"
import { startDownload } from "./lib/downloads"
import { formatDuration } from "./lib/format"
import { feedFetcher, feedKey, feedParams } from "./lib/items"
import { decodePeaks, type SfxBrowserSection } from "./lib/layout"
import { useMarketplace } from "./manifest"

export function Waveform({ peaks, progress }: { peaks: number[]; progress: number }) {
  return (
    <div className="flex h-8 items-center gap-px" aria-hidden>
      {peaks.map((h, i) => (
        <span
          key={i}
          className={cn("flex-1 rounded-full", (i + 1) / peaks.length <= progress ? "bg-primary" : "bg-muted-foreground/40")}
          style={{ height: `${Math.max(8, Math.round(h * 100))}%` }}
        />
      ))}
    </div>
  )
}

function SfxRow({ item }: { item: MarketItem }) {
  const audio = useSharedAudio()
  const mine = audio.currentId === item.id
  const playing = mine && audio.phase === "playing"
  const loading = mine && audio.phase === "loading"
  const peaks = useMemo(() => decodePeaks(item.extra?.peaks), [item.extra])
  const progress = mine ? audio.progress : 0
  return (
    <li className="panel flex items-center gap-3 p-3">
      <IconButton
        label={playing ? `Pause ${item.title}` : `Play ${item.title}`}
        disabled={!item.previewUrl}
        onClick={() => sharedAudio.toggle(item.id, item.previewUrl!)}
      >
        {loading ? <Spinner /> : playing ? <Pause className="size-4" aria-hidden /> : <Play className="size-4" aria-hidden />}
      </IconButton>
      <div className="min-w-0 flex-1">
        <p className="truncate font-medium">{item.title}</p>
        {peaks ? (
          <Waveform peaks={peaks} progress={progress} />
        ) : (
          <div className={cn("mt-1 h-1 overflow-hidden rounded-full bg-muted", !mine && "opacity-0")} aria-hidden>
            <div className="h-full bg-primary" style={{ width: `${progress * 100}%` }} />
          </div>
        )}
        {item.tags.length > 0 && <p className="truncate text-xs text-muted-foreground">{item.tags.join(", ")}</p>}
      </div>
      <span className="text-sm text-muted-foreground tabular-nums">{formatDuration(item.durationMs)}</span>
      <Button variant="secondary" size="sm" onClick={() => startDownload(item)} aria-label={`Download ${item.title}`}>
        <Download className="size-4" aria-hidden />
      </Button>
    </li>
  )
}

export function SfxBrowser({ section }: { section: SfxBrowserSection }) {
  useStopAudioOnLeave()
  const { source } = useMarketplace()
  const sourceId = section.source.id
  const categories = source(sourceId)?.categories ?? []
  const [q, setQ] = useState("")
  const [category, setCategory] = useState(section.source.category ?? "")
  const query = feedParams(section.source, { q: q.trim(), category })
  const feed = useInfinite(feedFetcher(sourceId, query), feedKey(sourceId, query))

  return (
    <div className="space-y-6">
      <div className="relative max-w-md">
        <Search className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-muted-foreground" aria-hidden />
        <Input value={q} onChange={(e) => setQ(e.target.value)} placeholder="Search sounds" aria-label="Search sounds" className="pl-9" />
      </div>
      {categories.length > 0 && <CategoryChips categories={categories} value={category} onChange={setCategory} />}
      <div aria-live="polite" className="space-y-3">
        {feed.error && <EmptyState title="Could not load sounds" description={feed.error.message} action={<Button onClick={feed.reload}>Try again</Button>} />}
        {!feed.error && !feed.loading && feed.items.length === 0 && <EmptyState title="No sounds match" description="Try a different search or category." />}
        <ul className="grid gap-2">
          {feed.items.map((i) => (
            <SfxRow key={i.id} item={i} />
          ))}
        </ul>
        {feed.loading && (
          <div className="grid gap-2">
            <CardSkeleton />
          </div>
        )}
        <div ref={feed.sentinel} className="h-10" />
      </div>
    </div>
  )
}
