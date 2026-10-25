import { stock, type StockItem, type StockType } from "@cutwire/api-client"
import { Badge, Button, EmptyState, Select, Skeleton, Spinner, toast } from "@cutwire/ui"
import { AlertTriangle, ArrowLeft, Check, Copy, Download, ExternalLink } from "lucide-react"
import { useState } from "react"
import { Link, useParams } from "react-router-dom"
import { formatBytes } from "../community/lib/text"
import { useAsync } from "../community/data"
import { Thumb } from "./cards"
import { loadCatalog, quotaRetry, useDownloadJob } from "./hooks"
import { DriftNote } from "./layout"
import { attributionText, formatDuration, retryText } from "./lib/format"

function Preview({ item, type }: { item: StockItem; type?: StockType }) {
  const kind = type?.mediaKind ?? (item.previewUrl?.match(/\.(mp3|ogg|wav|m4a|opus)(\?|$)/i) ? "audio" : "video")
  if (!item.previewUrl) return <Thumb src={item.thumbUrl} alt={item.title} />
  if (kind === "audio" || kind === "music" || kind === "sfx") {
    return (
      <div className="space-y-3">
        {item.thumbUrl && <Thumb src={item.thumbUrl} alt="" />}
        <audio src={item.previewUrl} controls preload="none" className="w-full" />
      </div>
    )
  }
  if (kind === "image" || kind === "overlay") {
    return <img src={item.previewUrl} alt={item.title} className="max-h-[70vh] w-full rounded-xl bg-muted object-contain" />
  }
  return <video src={item.previewUrl} poster={item.thumbUrl} controls playsInline preload="metadata" className="aspect-video w-full rounded-xl bg-black" />
}

function License({ item }: { item: StockItem }) {
  const [copied, setCopied] = useState(false)
  const text = attributionText(item)
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

function DownloadPanel({ item }: { item: StockItem }) {
  const variants = item.variants ?? []
  const [variant, setVariant] = useState("")
  const { job, error, busy, download } = useDownloadJob()
  const chosen = variant || variants[0]?.id
  const quota = quotaRetry(error)
  const failed = job?.status === "failed"
  const pct = Math.round((job?.progress ?? 0) * 100)

  if (!item.downloadable) {
    return <p className="text-sm text-muted-foreground">This item can be previewed here but not downloaded.</p>
  }
  return (
    <section className="space-y-3" aria-label="Download">
      {variants.length > 1 && (
        <Select
          value={chosen}
          onValueChange={setVariant}
          options={variants.map((v) => ({ value: v.id, label: `${v.label}${v.size ? ` (${formatBytes(v.size)})` : ""}` }))}
          aria-label="Variant"
        />
      )}
      <Button className="w-full" disabled={busy} onClick={() => download(item.id, chosen)}>
        {busy ? <Spinner /> : <Download className="size-4" aria-hidden />}
        {busy ? (job?.status === "processing" ? `Preparing ${pct}%` : "Starting...") : "Download"}
      </Button>
      {busy && (
        <div role="progressbar" aria-valuenow={pct} aria-valuemin={0} aria-valuemax={100} className="h-1.5 overflow-hidden rounded-full bg-muted">
          <div className="h-full bg-primary transition-[width]" style={{ width: `${pct}%` }} />
        </div>
      )}
      <div aria-live="polite" className="text-sm">
        {job?.status === "ready" && job.file && (
          <p className="text-muted-foreground">
            Your download should start automatically.{" "}
            <a href={job.file.url} download={job.file.filename} className="text-primary underline underline-offset-2">
              Download again
            </a>
          </p>
        )}
        {failed && <p className="text-destructive">{job?.error?.detail ?? "The download failed."}</p>}
        {quota.exceeded && <p className="text-destructive">Download limit reached. Try again {retryText(quota.retryAfter)}.</p>}
        {error && !quota.exceeded && <p className="text-destructive">{error.message}</p>}
      </div>
    </section>
  )
}

export function ItemPage() {
  const { id = "" } = useParams()
  const { data: item, loading, error, reload } = useAsync(() => stock.item(id), [id])
  const { data: catalog } = useAsync(loadCatalog, [])
  const type = catalog?.types.find((t) => t.id === item?.type)
  const providerLabel = type?.providers.find((p) => p.id === item?.provider)?.label ?? item?.provider

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

  const dims = item.width && item.height ? `${item.width}x${item.height}` : null
  return (
    <div className="space-y-6">
      <Button asChild variant="ghost" size="sm">
        <Link to={`/stock/search?type=${encodeURIComponent(item.type)}`}>
          <ArrowLeft className="size-4" aria-hidden /> Back to {type?.label ?? "search"}
        </Link>
      </Button>
      <div className="grid gap-8 lg:grid-cols-[1fr_22rem]">
        <div className="min-w-0">
          <Preview item={item} type={type} />
        </div>
        <div className="space-y-5">
          <div className="space-y-2">
            <h1 className="text-headline-md break-words">{item.title}</h1>
            {item.creator && (
              <p className="text-sm text-muted-foreground">
                by{" "}
                {item.creator.url ? (
                  <a href={item.creator.url} target="_blank" rel="noopener noreferrer" className="text-primary underline underline-offset-2">
                    {item.creator.name}
                  </a>
                ) : (
                  item.creator.name
                )}
              </p>
            )}
            <div className="flex flex-wrap gap-1.5">
              {type && <Badge tone="primary">{type.label}</Badge>}
              <Badge>{providerLabel}</Badge>
              {dims && <Badge>{dims}</Badge>}
              {item.durationMs != null && <Badge>{formatDuration(item.durationMs)}</Badge>}
            </div>
          </div>
          <DownloadPanel key={item.id} item={item} />
          <License item={item} />
          <DriftNote />
        </div>
      </div>
    </div>
  )
}
