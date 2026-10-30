import { marketplace, type MarketItem } from "@cutwire/api-client"
import { Badge, Button, EmptyState, Select, Skeleton, Spinner, toast } from "@cutwire/ui"
import { AlertTriangle, ArrowLeft, Check, Copy, Download, ExternalLink } from "lucide-react"
import { useState } from "react"
import { Link, useParams } from "react-router-dom"
import { useAsync } from "../community/data"
import { formatBytes } from "../community/lib/text"
import { metaFor, Thumb } from "./cards"
import { startDownload, useDownloads, itemKeyOf } from "./lib/downloads"
import { attributionText, formatDuration } from "./lib/format"
import { creatorOf, isCommunity, isDownloadable, providerOf } from "./lib/items"
import { resolveLink } from "./lib/layout"

function Preview({ item }: { item: MarketItem }) {
  const meta = metaFor(item.mediaKind)
  const thumb = item.thumbUrl || null
  if (!item.previewUrl) return <Thumb src={thumb} alt={item.title} icon={meta.icon} />
  if (item.mediaKind === "audio" || item.mediaKind === "music" || item.mediaKind === "audio-effect") {
    return (
      <div className="space-y-3">
        {thumb && <Thumb src={thumb} alt="" icon={meta.icon} />}
        <audio src={item.previewUrl} controls preload="none" className="w-full" aria-label={`Preview ${item.title}`} />
      </div>
    )
  }
  if (item.mediaKind === "image") {
    return <img src={item.previewUrl} alt={item.title} className="max-h-[70vh] w-full rounded-xl bg-muted object-contain" />
  }
  if (item.mediaKind === "video") {
    return <video src={item.previewUrl} poster={item.thumbUrl || undefined} controls playsInline preload="metadata" className="aspect-video w-full rounded-xl bg-black" />
  }
  return <Thumb src={thumb} alt={item.title} icon={meta.icon} />
}

function License({ item }: { item: MarketItem }) {
  const [copied, setCopied] = useState(false)
  const text = attributionText({ title: item.title, creator: creatorOf(item)?.name, license: item.license })
  const copy = async () => {
    try {
      await navigator.clipboard.writeText(text)
      setCopied(true)
      setTimeout(() => setCopied(false), 2000)
    } catch {
      toast.error("Could not copy to the clipboard")
    }
  }
  return (
    <section className="panel space-y-3 p-4" aria-label="License">
      <h2 className="text-label-md">License</h2>
      <p className="text-sm">
        {item.license.url ? (
          <a href={item.license.url} target="_blank" rel="noopener noreferrer" className="inline-flex items-center gap-1 text-primary underline underline-offset-2">
            {item.license.name} <ExternalLink className="size-3.5" aria-hidden />
          </a>
        ) : (
          item.license.name
        )}
      </p>
      <div className="flex items-start gap-2">
        <p className="min-w-0 flex-1 rounded-lg bg-muted p-2 text-sm break-words">{text}</p>
        <Button variant="secondary" size="sm" onClick={() => void copy()} aria-label="Copy attribution">
          {copied ? <Check className="size-4" aria-hidden /> : <Copy className="size-4" aria-hidden />}
          {copied ? "Copied" : "Copy"}
        </Button>
      </div>
    </section>
  )
}

function DownloadPanel({ item }: { item: MarketItem }) {
  const [variant, setVariant] = useState("")
  const entry = useDownloads().find((d) => d.itemKey === itemKeyOf(item))
  const chosen = variant || item.variants[0]?.id
  const busy = entry?.status === "starting" || entry?.status === "queued" || entry?.status === "processing"
  const pct = Math.round((entry?.progress ?? 0) * 100)

  if (isCommunity(item)) {
    const target = item.webUrl ? resolveLink(item.webUrl, window.location.origin) : null
    if (!target || target.kind === "blocked") return null
    return (
      <Button asChild className="w-full">
        {target.kind === "internal" ? <Link to={target.path}>View on Community</Link> : <a href={target.url}>View on Community</a>}
      </Button>
    )
  }
  if (!isDownloadable(item)) {
    return <p className="text-sm text-muted-foreground">This item can be previewed here but not downloaded.</p>
  }
  return (
    <section className="space-y-3" aria-label="Download">
      {item.variants.length > 1 && (
        <Select
          value={chosen}
          onValueChange={setVariant}
          options={item.variants.map((v) => ({ value: v.id, label: `${v.label}${v.size ? ` (${formatBytes(v.size)})` : ""}` }))}
          aria-label="Variant"
        />
      )}
      <Button className="w-full" disabled={busy} onClick={() => startDownload(item, chosen)}>
        {busy ? <Spinner /> : <Download className="size-4" aria-hidden />}
        {busy ? (entry?.status === "processing" ? `Preparing ${pct}%` : "Starting...") : "Download"}
      </Button>
      {busy && (
        <div role="progressbar" aria-label="Download progress" aria-valuenow={pct} aria-valuemin={0} aria-valuemax={100} className="h-1.5 overflow-hidden rounded-full bg-muted">
          <div className="h-full bg-primary transition-[width]" style={{ width: `${pct}%` }} />
        </div>
      )}
      <div aria-live="polite" className="text-sm">
        {entry?.status === "ready" && <p className="text-muted-foreground">Your download should start automatically. It is also listed under Downloads.</p>}
        {entry?.status === "failed" && <p className="text-destructive">{entry.error}</p>}
      </div>
    </section>
  )
}

export function ItemPage() {
  const { source = "", id = "" } = useParams()
  const { data: item, loading, error, reload } = useAsync(() => marketplace.getItem(source, id), [source, id])

  if (error) {
    return (
      <EmptyState
        icon={<AlertTriangle className="size-6" aria-hidden />}
        title="Could not load this item"
        description={error.message}
        action={<Button onClick={reload}>Try again</Button>}
      />
    )
  }
  if (loading || !item) return <Skeleton className="aspect-video w-full max-w-4xl rounded-xl" />

  const meta = metaFor(item.mediaKind)
  const creator = creatorOf(item)
  const provider = providerOf(item)
  const dims = item.width && item.height ? `${item.width}x${item.height}` : null
  return (
    <div className="space-y-6">
      <Button asChild variant="ghost" size="sm">
        <Link to="/marketplace">
          <ArrowLeft className="size-4" aria-hidden /> Back to the marketplace
        </Link>
      </Button>
      <div className="grid gap-8 lg:grid-cols-[1fr_22rem]">
        <div className="min-w-0">
          <Preview item={item} />
        </div>
        <div className="space-y-5">
          <div className="space-y-2">
            <h1 className="text-headline-md break-words">{item.title}</h1>
            {item.subtitle && <p className="text-sm text-muted-foreground">{item.subtitle}</p>}
            {creator && (
              <p className="text-sm text-muted-foreground">
                by{" "}
                {creator.url ? (
                  <a href={creator.url} target="_blank" rel="noopener noreferrer" className="text-primary underline underline-offset-2">
                    {creator.name}
                  </a>
                ) : (
                  creator.name
                )}
              </p>
            )}
            <div className="flex flex-wrap gap-1.5">
              <Badge tone="primary">{meta.label}</Badge>
              {provider && <Badge>{provider}</Badge>}
              {item.category && <Badge>{item.category}</Badge>}
              {dims && <Badge>{dims}</Badge>}
              {item.durationMs != null && <Badge>{formatDuration(item.durationMs)}</Badge>}
            </div>
          </div>
          <DownloadPanel key={`${item.source}/${item.id}`} item={item} />
          <License item={item} />
        </div>
      </div>
    </div>
  )
}
