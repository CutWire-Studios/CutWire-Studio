import { ApiError, community, num, type FieldViolation, type ItemDetail, type KindInfo } from "@cutwire/api-client"
import { Badge, Button, DropZone, Spinner } from "@cutwire/ui"
import { Check, ImagePlus } from "lucide-react"
import { useEffect, useRef, useState } from "react"
import { checkMedia, describeMediaProblem, isWebp, readWebpSize, type MediaProblem, type MediaSpec } from "../lib/media"
import { formatBytes } from "../lib/text"
import { violationsOf } from "../lib/violations"
import { MediaFixer } from "./media-fixer"
import { FieldErrors } from "./shared"

async function inspect(file: Blob) {
  const head = new Uint8Array(await file.slice(0, 64).arrayBuffer())
  const webp = isWebp(head)
  const size = webp ? readWebpSize(head) : null
  return { isWebp: webp, width: size?.width ?? null, height: size?.height ?? null, bytes: file.size }
}

function Slot({
  item,
  spec,
  initial,
  server,
  onItem,
}: {
  item: ItemDetail
  spec: MediaSpec
  initial?: Blob
  server: FieldViolation[]
  onItem: (i: ItemDetail) => void
}) {
  const existing = item.media.find((m) => m.role === spec.role)
  const [problems, setProblems] = useState<MediaProblem[]>([])
  const [serverProblems, setServerProblems] = useState<FieldViolation[]>([])
  const [source, setSource] = useState<Blob | null>(null)
  const [fixing, setFixing] = useState<Blob | null>(null)
  const [busy, setBusy] = useState(false)
  const tried = useRef(false)

  const upload = async (blob: Blob) => {
    setBusy(true)
    setServerProblems([])
    try {
      onItem(await community.uploadMedia(item.id, spec.role, blob))
      setProblems([])
      setSource(null)
    } catch (e) {
      const vs = violationsOf(e)
      setServerProblems(vs.length ? vs : [{ field: `media.${spec.role}`, code: "upload_failed", message: e instanceof ApiError ? e.title : "Upload failed." }])
    } finally {
      setBusy(false)
    }
  }

  const accept = async (file: Blob) => {
    const facts = await inspect(file)
    const found = checkMedia(facts, spec)
    setProblems(found)
    if (found.length === 0) await upload(file)
    else setSource(file)
  }

  useEffect(() => {
    if (initial && !existing && !tried.current) {
      tried.current = true
      void accept(initial)
    }
    // oxlint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  const mine = [...server.filter((v) => v.field === `media.${spec.role}`), ...serverProblems]

  return (
    <section className="panel space-y-3 p-4" aria-label={`${spec.role} image`}>
      <header className="flex flex-wrap items-center gap-2">
        <h3 className="font-semibold capitalize">{spec.role.replace("-", " ")}</h3>
        <Badge>{spec.required ? "Required" : "Optional"}</Badge>
        <span className="text-sm text-muted-foreground">
          {spec.width}x{spec.height} px, WebP, max {formatBytes(spec.maxBytes)}
        </span>
        {existing && !source && (
          <span className="ml-auto inline-flex items-center gap-1 text-sm text-green-600">
            <Check className="size-4" aria-hidden /> Uploaded
          </span>
        )}
      </header>
      {existing && (
        <img src={existing.url} alt={`Current ${spec.role}`} className="max-h-40 rounded-lg" style={{ aspectRatio: `${spec.width} / ${spec.height}` }} />
      )}
      <DropZone label={`Choose ${spec.role} image`} accept="image/*" disabled={busy} onFiles={(f) => void accept(f[0]!)} className="p-4">
        {busy ? <Spinner /> : <ImagePlus className="size-6 text-muted-foreground" aria-hidden />}
        <p className="text-sm">{existing ? "Drop a new image to replace it" : "Drop an image or click to browse"}</p>
      </DropZone>
      {problems.length > 0 && (
        <div className="space-y-2" role="alert">
          <ul className="space-y-1 text-sm text-destructive">
            {problems.map((p) => (
              <li key={p.code}>{describeMediaProblem(p, formatBytes)}</li>
            ))}
          </ul>
          {source && (
            <Button size="sm" onClick={() => setFixing(source)}>
              Fix it for me
            </Button>
          )}
        </div>
      )}
      <FieldErrors violations={mine} />
      {fixing && (
        <MediaFixer
          source={fixing}
          spec={spec}
          onClose={() => setFixing(null)}
          onAccept={(b) => {
            setFixing(null)
            void upload(b)
          }}
        />
      )}
    </section>
  )
}

export function MediaStep({
  item,
  kind,
  handoffMedia,
  server,
  onItem,
}: {
  item: ItemDetail
  kind: KindInfo
  handoffMedia: Record<string, Blob>
  server: FieldViolation[]
  onItem: (i: ItemDetail) => void
}) {
  return (
    <div className="space-y-4">
      {kind.media.map((spec) => (
        <Slot
          key={spec.role}
          item={item}
          spec={{ ...spec, width: num(spec.width), height: num(spec.height), maxBytes: num(spec.maxBytes) }}
          // A screenshot-style handoff image can seed the thumbnail through the fixer.
          initial={handoffMedia[spec.role] ?? (spec.role === "thumbnail" ? handoffMedia.screenshot : undefined)}
          server={server}
          onItem={onItem}
        />
      ))}
    </div>
  )
}
