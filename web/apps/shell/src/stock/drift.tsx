import { stock, type DriftAsset } from "@cutwire/api-client"
import { Badge, Button, EmptyState, Skeleton } from "@cutwire/ui"
import { Download } from "lucide-react"
import { formatBytes } from "../community/lib/text"
import { useAsync } from "../community/data"
import { Thumb } from "./cards"
import { triggerDownload } from "./hooks"
import { DriftNote } from "./layout"

const KIND_LABELS: Record<DriftAsset["kind"], string> = { lottie: "Animation", "face-prop": "Face prop", model3d: "3D model" }
const isVideo = (u: string) => /\.(mp4|webm|mov)(\?|$)/i.test(u)

function AssetPreview({ asset }: { asset: DriftAsset }) {
  const src = asset.previewUrl
  if (src && isVideo(src)) {
    return <video src={src} poster={asset.thumbnailUrl} autoPlay loop muted playsInline aria-label={asset.title} className="aspect-video w-full rounded-xl bg-muted object-cover" />
  }
  return <Thumb src={src ?? asset.thumbnailUrl} alt={asset.title} />
}

function AssetCard({ asset }: { asset: DriftAsset }) {
  return (
    <article className="panel flex h-full flex-col gap-3 p-3">
      <div className="relative">
        <AssetPreview asset={asset} />
        <div className="absolute top-2 left-2">
          <Badge tone="primary">{KIND_LABELS[asset.kind]}</Badge>
        </div>
      </div>
      <h3 className="min-w-0 flex-1 truncate font-semibold" title={asset.title}>
        {asset.title}
      </h3>
      <div className="flex flex-wrap gap-2">
        {asset.variants.map((v) => (
          <Button key={v.id} variant="secondary" size="sm" onClick={() => triggerDownload(v.downloadUrl)} aria-label={`Download ${asset.title} ${v.label}`}>
            <Download className="size-4" aria-hidden /> {v.label} <span className="text-muted-foreground">{formatBytes(v.size)}</span>
          </Button>
        ))}
      </div>
    </article>
  )
}

export function DriftAssetsPage() {
  const { data, loading, error, reload } = useAsync(stock.driftAssets, [])
  return (
    <div className="space-y-8">
      <div className="space-y-2">
        <h1 className="text-headline-lg">Drift assets</h1>
        <p className="text-muted-foreground">Animations, face props and 3D models.</p>
        <DriftNote />
      </div>
      {loading && !data && <Skeleton className="h-48 w-full rounded-xl" />}
      {error && <EmptyState title="Could not load assets" description={error.message} action={<Button onClick={reload}>Try again</Button>} />}
      {data?.categories.map((c) => (
        <section key={c.id} className="space-y-3" aria-label={c.label}>
          <h2 className="text-headline-md">{c.label}</h2>
          <ul className="grid gap-4 [grid-template-columns:repeat(auto-fill,minmax(15rem,1fr))]">
            {c.assets.map((a) => (
              <li key={a.id}>
                <AssetCard asset={a} />
              </li>
            ))}
          </ul>
        </section>
      ))}
    </div>
  )
}
