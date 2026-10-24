import { ApiError, community, type FieldViolation, type ItemDetail } from "@cutwire/api-client"
import type { BloomFilter } from "@cutwire/content-rules"
import { Button, EmptyState, Spinner, toast } from "@cutwire/ui"
import { ArrowLeft, ArrowRight, Rocket, X } from "lucide-react"
import { useEffect, useMemo, useRef, useState } from "react"
import { useNavigate, useSearchParams } from "react-router-dom"
import { RequireAuth, useMe } from "../../account/auth"
import { asRules, loadBloom, useKinds, useRules } from "../data"
import { clearHandoff, readHandoff, type Handoff } from "../lib/handoff"
import { FLAG_CODES, liveCheck } from "../lib/live-check"
import { STEPS, firstStepWithViolations, groupByStep, violationsOf, type Step } from "../lib/violations"
import { DetailsStep, EMPTY_DETAILS, type Details } from "./details-step"
import { MediaStep } from "./media-step"
import { PackageStep } from "./package-step"
import { ReviewStep } from "./review-step"
import { FieldErrors, NoticeList, Stepper } from "./shared"

export function UploadPage() {
  return (
    <RequireAuth>
      <Wizard />
    </RequireAuth>
  )
}

const detailsOf = (i: ItemDetail): Details => ({
  title: i.title,
  summary: i.summary,
  description: i.description,
  category: i.category,
  tags: i.tags,
  license: i.license,
})

function Wizard() {
  const [params] = useSearchParams()
  const navigate = useNavigate()
  const { me } = useMe()
  const itemParam = params.get("item")
  const kindParam = params.get("kind")
  const kindsQ = useKinds()
  const rulesQ = useRules()

  const [step, setStep] = useState<Step>("package")
  const [kind, setKind] = useState(kindParam ?? "")
  const [file, setFile] = useState<File | null>(null)
  const [item, setItem] = useState<ItemDetail | null>(null)
  const [details, setDetails] = useState<Details>(EMPTY_DETAILS)
  const [server, setServer] = useState<FieldViolation[]>([])
  const [pkgProblems, setPkgProblems] = useState<FieldViolation[]>([])
  const [clientError, setClientError] = useState<string | null>(null)
  const [handoff, setHandoff] = useState<Handoff | null>(null)
  const [busy, setBusy] = useState(false)
  const [bloom, setBloom] = useState<BloomFilter | null>(null)
  const [bloomState, setBloomState] = useState<"idle" | "loading" | "ready" | "error">("idle")
  const [loadError, setLoadError] = useState<Error | null>(null)
  const [booted, setBooted] = useState(false)
  const itemRef = useRef<ItemDetail | null>(null)
  itemRef.current = item

  useEffect(() => {
    if (itemRef.current && itemRef.current.id === itemParam) return
    let live = true
    ;(async () => {
      if (itemParam) {
        const it = await community.getById(itemParam)
        if (!live) return
        if (it.status !== "draft") {
          navigate(`/community/edit/${it.id}`, { replace: true })
          return
        }
        setItem(it)
        setKind(it.kind)
        setDetails(detailsOf(it))
        setStep(it.versions.length ? "details" : "package")
      } else {
        const h = await readHandoff(kindParam)
        if (!live) return
        if (h) {
          setHandoff(h)
          setKind(h.kind)
          setFile(new File([h.packageBlob], h.filename, { type: h.packageBlob.type }))
          setDetails((d) => ({ ...d, title: h.title ?? d.title, description: h.description ?? d.description }))
        }
      }
      setBooted(true)
    })().catch((e: Error) => live && (setLoadError(e), setBooted(true)))
    return () => {
      live = false
    }
  }, [itemParam, kindParam, navigate])

  const kinds = kindsQ.data
  const rules = rulesQ.data ? asRules(rulesQ.data) : null
  const kindInfo = kinds?.find((k) => k.id === kind)

  const wantBloom = () => {
    if (bloomState !== "idle" || !rulesQ.data) return
    setBloomState("loading")
    loadBloom(rulesQ.data).then(
      (b) => (setBloom(b), setBloomState("ready")),
      () => setBloomState("error"),
    )
  }

  // Resumed drafts and handoffs already have prose, so start the word-list fetch right away.
  useEffect(() => {
    if (details.description && rulesQ.data) wantBloom()
    // oxlint-disable-next-line react-hooks/exhaustive-deps
  }, [booted, rulesQ.data])

  const live = useMemo(() => (rules && kind ? liveCheck(rules, bloom, { kind, ...details }) : []), [rules, bloom, kind, details])

  const hasPackage = !!file || !!item?.versions.length
  const localIssues = useMemo(() => {
    const out: { step: Step; message: string }[] = []
    if (!hasPackage) out.push({ step: "package", message: "A package file is required." })
    for (const v of live) out.push({ step: "details", message: v.message })
    for (const spec of kindInfo?.media ?? []) {
      if (spec.required && !item?.media.some((m) => m.role === spec.role)) out.push({ step: "media", message: `The ${spec.role} image is required.` })
    }
    return out
  }, [hasPackage, live, kindInfo, item])

  const grouped = groupByStep(server.filter((v) => !FLAG_CODES.has(v.code)))
  const issues: Partial<Record<Step, number>> = {
    package: pkgProblems.length + grouped.package.length,
    details: live.length > 0 && step !== "package" && step !== "details" ? live.length : grouped.details.length,
    media: grouped.media.length,
    review: grouped.review.length,
  }
  const reachable: Step[] = ["package", ...(kindInfo ? (["details"] as Step[]) : []), ...(item ? (["media", "review"] as Step[]) : [])]

  const goBackTo = (s: Step) => setStep(s)

  const saveAndContinue = async () => {
    if (!kindInfo) return
    setBusy(true)
    setServer([])
    let phase: "metadata" | "package" = "metadata"
    try {
      let it = item
      if (!it) {
        it = await community.create({ kind, ...details })
        setItem(it)
        navigate(`/community/upload?item=${it.id}`, { replace: true })
      } else it = await community.update(it.id, details)
      setItem(it)
      if (file) {
        phase = "package"
        it = await community.uploadPackage(it.id, file)
        setItem(it)
        setFile(null)
        setPkgProblems([])
      }
      setStep("media")
    } catch (e) {
      const vs = violationsOf(e)
      if (phase === "package") {
        setPkgProblems(vs.length ? vs : [{ field: "package", code: "upload_failed", message: e instanceof ApiError ? e.title : "Upload failed." }])
        setStep("package")
      } else if (vs.length) {
        setServer(vs)
      } else toast.error(e instanceof ApiError ? e.detail ?? e.title : "Could not save the draft.")
    } finally {
      setBusy(false)
    }
  }

  const publish = async () => {
    if (!item) return
    setBusy(true)
    setServer([])
    try {
      const done = await community.publish(item.id)
      if (handoff) await clearHandoff(handoff.key)
      const flags = (done as { flags?: unknown[] }).flags
      toast.success(flags?.length ? "Published. A moderator will take a quick look at a few notices." : "Published!")
      navigate(`/community/${done.kind}/${done.slug}`)
    } catch (e) {
      const vs = violationsOf(e)
      if (vs.length) {
        setServer(vs)
        const target = firstStepWithViolations(vs.filter((v) => !FLAG_CODES.has(v.code)))
        if (target && target !== "review") toast.error("Some things need fixing before this can be published.")
      } else toast.error(e instanceof ApiError ? e.detail ?? e.title : "Could not publish.")
    } finally {
      setBusy(false)
    }
  }

  const discardHandoff = async () => {
    if (handoff) await clearHandoff(handoff.key)
    setHandoff(null)
    setFile(null)
  }

  if (loadError) {
    return <EmptyState title="Could not open this draft" description={loadError.message} />
  }
  if (!booted || kindsQ.loading || rulesQ.loading) return <Spinner className="mx-auto mt-24" />
  if (!kinds || !rules) return <EmptyState title="Could not load upload rules" description={(kindsQ.error ?? rulesQ.error)?.message} />

  const enabledKinds = kinds
  const idx = STEPS.findIndex((s) => s.id === step)
  const prev = STEPS[idx - 1]?.id
  const detailsBlocked = live.length > 0 || !hasPackage

  return (
    <div className="mx-auto max-w-3xl space-y-6">
      <header className="space-y-1">
        <h1 className="text-headline-lg">{item ? "Finish your draft" : "Share something"}</h1>
        <p className="text-muted-foreground">Your item is checked against the same quality rules the server uses, as you type.</p>
      </header>

      {me && !me.emailVerified && (
        <div role="alert" className="rounded-lg border border-border bg-muted px-3 py-2 text-sm">
          Verify your email address before publishing. You can still prepare a draft.
        </div>
      )}

      {handoff && (
        <div role="status" className="flex flex-wrap items-center justify-between gap-2 rounded-lg border border-border bg-muted px-3 py-2 text-sm">
          <span>
            Imported <strong>{handoff.filename}</strong>
            {handoff.source ? ` from ${handoff.source === "themes" ? "Theme Maker" : handoff.source}` : ""}.
          </span>
          <Button variant="ghost" size="sm" onClick={() => void discardHandoff()}>
            <X className="size-4" aria-hidden /> Discard
          </Button>
        </div>
      )}

      <Stepper step={step} reachable={reachable} issues={issues} onGo={goBackTo} />

      <section className="panel space-y-6 p-5 sm:p-6" aria-live="polite">
        {step === "package" && (
          <PackageStep
            kinds={enabledKinds}
            kind={kind}
            onKind={setKind}
            file={file}
            onFile={setFile}
            item={item}
            problems={[...pkgProblems, ...grouped.package]}
            clientError={clientError}
            setClientError={setClientError}
            lockKind={!!item}
          />
        )}
        {step === "details" && kindInfo && (
          <DetailsStep kind={kindInfo} rules={rules} value={details} onChange={setDetails} live={live} server={grouped.details} bloomState={bloomState} onWantBloom={wantBloom} />
        )}
        {step === "media" && item && kindInfo && <MediaStep item={item} kind={kindInfo} handoffMedia={handoff?.media ?? {}} server={server} onItem={setItem} />}
        {step === "review" && item && kindInfo && (
          <ReviewStep kind={kindInfo} details={details} item={item} localIssues={localIssues} violations={server} onGo={goBackTo} />
        )}
        {step !== "review" && <FieldErrors violations={grouped.review} />}
        {step !== "review" && <NoticeList violations={server.filter((v) => FLAG_CODES.has(v.code))} />}

        <footer className="flex items-center justify-between gap-2 border-t border-border pt-4">
          <Button variant="ghost" disabled={!prev || busy} onClick={() => prev && setStep(prev)}>
            <ArrowLeft className="size-4" aria-hidden /> Back
          </Button>
          {step === "package" && (
            <Button disabled={!kindInfo || !hasPackage || !!clientError} onClick={() => setStep("details")}>
              Continue <ArrowRight className="size-4" aria-hidden />
            </Button>
          )}
          {step === "details" && (
            <Button disabled={detailsBlocked || busy} onClick={() => void saveAndContinue()}>
              {busy && <Spinner className="size-4" />} Save and continue <ArrowRight className="size-4" aria-hidden />
            </Button>
          )}
          {step === "media" && (
            <Button onClick={() => setStep("review")}>
              Review <ArrowRight className="size-4" aria-hidden />
            </Button>
          )}
          {step === "review" && (
            <Button disabled={busy || localIssues.length > 0} onClick={() => void publish()}>
              {busy ? <Spinner className="size-4" /> : <Rocket className="size-4" aria-hidden />} Publish
            </Button>
          )}
        </footer>
      </section>
    </div>
  )
}


