import { stock, type SfxSound } from "@cutwire/api-client"
import { Button, EmptyState, IconButton, Input, Skeleton, Spinner, cn } from "@cutwire/ui"
import { Download, Pause, Play, Search } from "lucide-react"
import { useMemo, useState } from "react"
import { formatBytes } from "../community/lib/text"
import { useAsync } from "../community/data"
import { sharedAudio, triggerDownload, useSharedAudio, useStopAudioOnLeave } from "./hooks"
import { DriftNote } from "./layout"
import { formatDuration } from "./lib/format"

export function matchesSound(s: SfxSound, q: string): boolean {
  const n = q.trim().toLowerCase()
  return !n || s.title.toLowerCase().includes(n) || s.tags.some((t) => t.toLowerCase().includes(n))
}

function SfxRow({ sound }: { sound: SfxSound }) {
  const audio = useSharedAudio()
  const mine = audio.currentId === sound.id
  const playing = mine && audio.phase === "playing"
  const loading = mine && audio.phase === "loading"
  return (
    <li className="panel flex items-center gap-3 p-3">
      <IconButton label={playing ? `Pause ${sound.title}` : `Play ${sound.title}`} onClick={() => sharedAudio.toggle(sound.id, sound.previewUrl)}>
        {loading ? <Spinner /> : playing ? <Pause className="size-4" aria-hidden /> : <Play className="size-4" aria-hidden />}
      </IconButton>
      <div className="min-w-0 flex-1">
        <p className="truncate font-medium">{sound.title}</p>
        <div className={cn("mt-1 h-1 overflow-hidden rounded-full bg-muted", !mine && "opacity-0")} aria-hidden>
          <div className="h-full bg-primary" style={{ width: `${mine ? audio.progress * 100 : 0}%` }} />
        </div>
        {sound.tags.length > 0 && <p className="mt-1 truncate text-xs text-muted-foreground">{sound.tags.join(", ")}</p>}
      </div>
      <span className="text-sm text-muted-foreground tabular-nums">{formatDuration(sound.durationMs)}</span>
      <Button variant="secondary" size="sm" onClick={() => triggerDownload(sound.downloadUrl)} aria-label={`Download ${sound.title}`}>
        <Download className="size-4" aria-hidden />
        <span className="hidden sm:inline">{formatBytes(sound.size)}</span>
      </Button>
    </li>
  )
}

export function SfxPage() {
  useStopAudioOnLeave()
  const { data, loading, error, reload } = useAsync(stock.sfx, [])
  const [q, setQ] = useState("")
  const [cat, setCat] = useState("")
  const categories = data?.categories ?? []
  const visible = useMemo(
    () =>
      categories
        .filter((c) => !cat || c.id === cat)
        .map((c) => ({ ...c, sounds: c.sounds.filter((s) => matchesSound(s, q)) }))
        .filter((c) => c.sounds.length),
    [categories, cat, q],
  )

  return (
    <div className="space-y-6">
      <div className="space-y-2">
        <h1 className="text-headline-lg">Sound effects</h1>
        <DriftNote />
      </div>
      <div className="relative max-w-md">
        <Search className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-muted-foreground" aria-hidden />
        <Input value={q} onChange={(e) => setQ(e.target.value)} placeholder="Filter sounds" aria-label="Filter sounds" className="pl-9" />
      </div>
      <div className="flex flex-wrap gap-1.5" aria-label="Categories">
        {[{ id: "", label: "All" }, ...categories].map((c) => (
          <button
            key={c.id}
            type="button"
            aria-pressed={cat === c.id}
            onClick={() => setCat(c.id)}
            className={cn("text-label-md cursor-pointer rounded-full border px-4 py-1.5 transition-colors", cat === c.id ? "border-primary bg-primary text-primary-foreground" : "border-border hover:bg-muted")}
          >
            {c.label}
          </button>
        ))}
      </div>
      {loading && !data && <Skeleton className="h-48 w-full rounded-xl" />}
      {error && <EmptyState title="Could not load sounds" description={error.message} action={<Button onClick={reload}>Try again</Button>} />}
      {data && visible.length === 0 && <EmptyState title="No sounds match" description="Try a different search or category." />}
      {visible.map((c) => (
        <section key={c.id} className="space-y-3" aria-label={c.label}>
          <h2 className="text-headline-md">{c.label}</h2>
          <ul className="grid gap-2">
            {c.sounds.map((s) => (
              <SfxRow key={s.id} sound={s} />
            ))}
          </ul>
        </section>
      ))}
    </div>
  )
}
