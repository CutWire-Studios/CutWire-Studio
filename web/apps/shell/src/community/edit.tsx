import { ApiError, community, type FieldViolation, type ItemDetail, type KindInfo, type QualityRulesJson } from "@cutwire/api-client"
import { Button, DropZone, EmptyState, Input, Skeleton, Spinner, toast } from "@cutwire/ui"
import type { BloomFilter, QualityRules } from "@cutwire/content-rules"
import { FileBox } from "lucide-react"
import { useMemo, useState } from "react"
import { Link, useParams } from "react-router-dom"
import { RequireAuth } from "../account/auth"
import { asRules, loadBloom, useAsync, useKinds, useRules } from "./data"
import { liveCheck } from "./lib/live-check"
import { formatBytes } from "./lib/text"
import { violationsOf } from "./lib/violations"
import { DetailsStep, type Details } from "./upload/details-step"
import { MediaStep } from "./upload/media-step"
import { checkPackageFile } from "./upload/package-step"
import { FieldErrors } from "./upload/shared"

export function EditItemPage() {
  return (
    <RequireAuth>
      <Editor />
    </RequireAuth>
  )
}

function Editor() {
  const { id = "" } = useParams()
  const q = useAsync(() => community.getById(id), [id])
  const kindsQ = useKinds()
  const rulesQ = useRules()
  if (q.loading || kindsQ.loading || rulesQ.loading) return <Skeleton className="mx-auto h-96 max-w-3xl" />
  if (!q.data || !kindsQ.data || !rulesQ.data) {
    return <EmptyState title="Could not open this item" description={(q.error ?? kindsQ.error ?? rulesQ.error)?.message} />
  }
  return <EditForm item={q.data} kinds={kindsQ.data} rawRules={rulesQ.data} onItem={q.set} />
}

function EditForm({ item, kinds, rawRules, onItem }: { item: ItemDetail; kinds: KindInfo[]; rawRules: QualityRulesJson; onItem: (i: ItemDetail) => void }) {
  const rules = asRules(rawRules)
  const kind = kinds.find((k) => k.id === item.kind)
  if (!kind) return <EmptyState title="This kind is no longer available" />
  return <EditBody item={item} kind={kind} rawRules={rawRules} rules={rules} onItem={onItem} />
}

function EditBody({ item, kind, rawRules, rules, onItem }: { item: ItemDetail; kind: KindInfo; rawRules: QualityRulesJson; rules: QualityRules; onItem: (i: ItemDetail) => void }) {
  const [details, setDetails] = useState<Details>({ title: item.title, summary: item.summary, description: item.description, category: item.category, tags: item.tags, license: item.license })
  const [bloom, setBloom] = useState<BloomFilter | null>(null)
  const [bloomState, setBloomState] = useState<"idle" | "loading" | "ready" | "error">("idle")
  const [server, setServer] = useState<FieldViolation[]>([])
  const [saving, setSaving] = useState(false)
  const [file, setFile] = useState<File | null>(null)
  const [fileError, setFileError] = useState<string | null>(null)
  const [changelog, setChangelog] = useState("")
  const [versionProblems, setVersionProblems] = useState<FieldViolation[]>([])
  const [uploading, setUploading] = useState(false)

  const live = useMemo(() => liveCheck(rules, bloom, { kind: item.kind, ...details }), [rules, bloom, item.kind, details])

  const wantBloom = () => {
    if (bloomState !== "idle") return
    setBloomState("loading")
    loadBloom(rawRules).then(
      (b) => (setBloom(b), setBloomState("ready")),
      () => setBloomState("error"),
    )
  }

  const save = async () => {
    setSaving(true)
    setServer([])
    try {
      onItem(await community.update(item.id, details))
      toast.success("Saved.")
    } catch (e) {
      const vs = violationsOf(e)
      if (vs.length) setServer(vs)
      else toast.error(e instanceof ApiError ? e.detail ?? e.title : "Could not save.")
    } finally {
      setSaving(false)
    }
  }

  const addVersion = async () => {
    if (!file) return
    setUploading(true)
    setVersionProblems([])
    try {
      onItem(await community.uploadVersion(item.id, file, changelog.trim() || undefined))
      setFile(null)
      setChangelog("")
      toast.success("New version uploaded.")
    } catch (e) {
      const vs = violationsOf(e)
      setVersionProblems(vs.length ? vs : [{ field: "package", code: "upload_failed", message: e instanceof ApiError ? e.title : "Upload failed." }])
    } finally {
      setUploading(false)
    }
  }

  const current = item.versions.find((v) => v.isCurrent) ?? item.versions[0]

  return (
    <div className="mx-auto max-w-3xl space-y-8">
      <header className="space-y-1">
        <h1 className="text-headline-lg">Edit {item.title}</h1>
        <Link to={`/community/${item.kind}/${item.slug}`} className="text-primary underline underline-offset-2">
          View item
        </Link>
      </header>

      <section className="panel space-y-6 p-5 sm:p-6">
        <h2 className="text-headline-md">Details</h2>
        <DetailsStep kind={kind} rules={rules} value={details} onChange={setDetails} live={live} server={server} bloomState={bloomState} onWantBloom={wantBloom} />
        <div className="flex justify-end">
          <Button disabled={saving || live.length > 0} onClick={() => void save()}>
            {saving && <Spinner className="size-4" />} Save changes
          </Button>
        </div>
      </section>

      <section className="panel space-y-4 p-5 sm:p-6">
        <h2 className="text-headline-md">Images</h2>
        <MediaStep item={item} kind={kind} handoffMedia={{}} server={[]} onItem={onItem} />
      </section>

      <section className="panel space-y-4 p-5 sm:p-6">
        <h2 className="text-headline-md">Upload a new version</h2>
        {current && <p className="text-sm text-muted-foreground">Current version is {current.version}. The new package must have a higher version in its manifest.</p>}
        <DropZone
          label="Choose package file"
          onFiles={(f) => {
            const err = checkPackageFile(f[0]!, kind)
            setFileError(err)
            setFile(err ? null : f[0]!)
          }}
        >
          <FileBox className="size-6 text-muted-foreground" aria-hidden />
          <p className="text-sm">{file ? `${file.name} (${formatBytes(file.size)})` : `Drop a ${kind.acceptedExtensions.join(" / ")} file`}</p>
        </DropZone>
        {fileError && <p role="alert" className="text-sm text-destructive">{fileError}</p>}
        <Input value={changelog} onChange={(e) => setChangelog(e.target.value)} placeholder="What changed? (optional)" aria-label="Changelog" maxLength={500} />
        <FieldErrors violations={versionProblems} />
        <div className="flex justify-end">
          <Button disabled={!file || uploading} onClick={() => void addVersion()}>
            {uploading && <Spinner className="size-4" />} Upload version
          </Button>
        </div>
      </section>
    </div>
  )
}
