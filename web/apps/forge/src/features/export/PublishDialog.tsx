import { useEffect, useMemo, useRef, useState } from "react"
import { errorMessage } from "@cutwire/forge-core/errors"
import type { ForgeDoc } from "@cutwire/forge-core/doc/types"
import { exportDriftfx } from "@cutwire/forge-core/export/archive"
import { signIn } from "@/services/account/client"
import { stripBlobRefs, useSync } from "@/services/cloud"
import { captureFrames, encodeWebp, type CommunityFrames } from "@/services/publish/capture"
import { buildHandoff, communityKind, uploadUrl, writeHandoff } from "@/services/publish/handoff"
import { fitWebp, KIND_MEDIA, type WebpFit } from "@/services/publish/webp"
import { useDoc } from "@/state/editor"
import { Modal } from "@/shared/ui/Modal"
import { toast } from "@/shared/ui/toast"
import { previewPng } from "./previewPng"

const kb = (n: number) => `${(n / 1024).toFixed(1)} KB`

/** Encodes `img` as WebP under `cap` bytes, automatically at first and then at the slider's quality. */
function useFittedImage(img: ImageData | undefined, cap: number) {
  const [state, setState] = useState<{ img: ImageData | undefined; fit: WebpFit | null; error: string | null }>({ img, fit: null, error: null })
  const run = useRef(0)

  const start = (encode: (img: ImageData) => Promise<WebpFit>) => {
    if (!img) return
    const id = ++run.current
    encode(img)
      .then((fit) => id === run.current && setState({ img, fit, error: null }))
      .catch((e) => id === run.current && setState({ img, fit: null, error: errorMessage(e) }))
  }
  const auto = () => start((i) => fitWebp((q) => encodeWebp(i, q), cap))
  const setQuality = (quality: number) =>
    start(async (i) => {
      const blob = await encodeWebp(i, quality)
      return { blob, quality, bytes: blob.size, fits: blob.size <= cap }
    })
  useEffect(auto, [img, cap]) // eslint-disable-line react-hooks/exhaustive-deps

  const current = state.img === img
  return { fit: current ? state.fit : null, error: current ? state.error : null, auto, setQuality }
}

function MediaFit({
  label,
  spec,
  img,
  state,
}: {
  label: string
  spec: { width: number; height: number; cap: number }
  img: ImageData | undefined
  state: ReturnType<typeof useFittedImage>
}) {
  const { fit } = state
  const url = useMemo(() => (fit ? URL.createObjectURL(fit.blob) : null), [fit])
  useEffect(() => () => void (url && URL.revokeObjectURL(url)), [url])
  return (
    <div className="publish-media">
      <strong>{label}</strong>
      <span className="meta small">
        {spec.width}×{spec.height} WebP, at most {kb(spec.cap)}
      </span>
      {url ? <img src={url} alt={label} width={spec.width} height={spec.height} className="publish-img" /> : <div className="skeleton publish-img" />}
      {state.error ? (
        <p className="meta small danger-text">{state.error}</p>
      ) : (
        fit && (
          <>
            <label className="publish-slider">
              <span className="meta small">Quality {Math.round(fit.quality * 100)}%</span>
              <input
                type="range"
                min={5}
                max={100}
                value={Math.round(fit.quality * 100)}
                disabled={!img}
                onChange={(e) => state.setQuality(Number(e.target.value) / 100)}
              />
            </label>
            <span className={`meta small${fit.fits ? "" : " danger-text"}`}>
              {kb(fit.bytes)} of {kb(spec.cap)}
              {fit.fits ? "" : " (too big, lower the quality)"}
            </span>
            <button type="button" className="link-btn" onClick={state.auto}>
              Pick the best quality automatically
            </button>
          </>
        )
      )}
    </div>
  )
}

async function handOff(doc: ForgeDoc, media: { thumbnail: Blob; previewStrip?: Blob }, title: string, description: string) {
  const clean = stripBlobRefs(doc)
  const bytes = await exportDriftfx(clean, { png: doc.kind === "audio" ? null : await previewPng(clean) })
  const base = doc.meta.displayName.replace(/[^\w\- ]+/g, "").trim() || doc.meta.id
  await writeHandoff(buildHandoff({ kind: doc.kind, packageBytes: bytes, filename: `${base}.driftfx`, ...media, title, description }))
  location.assign(uploadUrl(communityKind(doc.kind)))
}

function publishLabel(signedIn: boolean, busy: boolean): string {
  if (!signedIn) return "Sign in to publish"
  return busy ? "Preparing…" : "Continue to upload"
}

function PublishForm({ doc, frames }: { doc: ForgeDoc; frames: CommunityFrames }) {
  const signedIn = useSync((s) => s.signedIn)
  const thumb = useFittedImage(frames.thumbnail, KIND_MEDIA.thumbnail.cap)
  const strip = useFittedImage(frames.strip, KIND_MEDIA.strip.cap)
  const [title, setTitle] = useState(doc.meta.displayName)
  const [description, setDescription] = useState(doc.meta.description ?? "")
  const [busy, setBusy] = useState(false)
  const needsStrip = doc.kind === "transition"
  const ready = !!thumb.fit?.fits && (!needsStrip || !!strip.fit?.fits)

  const publish = async () => {
    if (!signedIn) return signIn()
    if (!thumb.fit) return
    setBusy(true)
    try {
      await handOff(doc, { thumbnail: thumb.fit.blob, previewStrip: strip.fit?.blob }, title.trim(), description.trim())
    } catch (e) {
      toast(errorMessage(e), "error")
      setBusy(false)
    }
  }

  return (
    <>
      <p className="meta">
        Nothing is published yet. The upload page opens with your package and pictures filled in, and you finish the details there.
      </p>
      <div className="publish-grid">
        <MediaFit label="Thumbnail" spec={KIND_MEDIA.thumbnail} img={frames.thumbnail} state={thumb} />
        {needsStrip && <MediaFit label="Preview strip" spec={KIND_MEDIA.strip} img={frames.strip} state={strip} />}
      </div>
      <label className="field">
        <span>Title</span>
        <input className="input input-sm" value={title} onChange={(e) => setTitle(e.target.value)} />
      </label>
      <label className="field">
        <span>Description</span>
        <textarea className="input textarea" rows={3} value={description} onChange={(e) => setDescription(e.target.value)} />
      </label>
      <div className="publish-actions">
        {!signedIn && <span className="meta small">You need to sign in to publish. You'll come back here afterwards.</span>}
        <button type="button" className="btn btn-primary btn-sm" disabled={busy || (signedIn && !ready)} onClick={() => void publish()}>
          {publishLabel(signedIn, busy)}
        </button>
      </div>
    </>
  )
}

/** Builds the package and its community media, then hands them to the upload page. */
export function PublishDialog({ doc, onClose }: { doc: ForgeDoc; onClose: () => void }) {
  const frames = useMemo<CommunityFrames | string>(() => captureFrames(doc), [doc])
  return (
    <Modal title="Publish to Community" onClose={onClose} wide>
      <div className="stack gap-3">
        {typeof frames === "string" ? (
          <p className="meta danger-text" role="alert">
            The preview couldn't be rendered: {frames}
          </p>
        ) : (
          <PublishForm doc={doc} frames={frames} />
        )}
      </div>
    </Modal>
  )
}

/** The Export dialog's "Publish to Community" card. */
export function PublishCard({ doc }: { doc: ForgeDoc }) {
  const [open, setOpen] = useState(false)
  return (
    <div className="export-card">
      <div>
        <h4>Publish to Community</h4>
        <p className="meta">Share it with everyone. Builds the package and the exact-size pictures, then opens the upload form.</p>
      </div>
      <button type="button" className="btn btn-secondary btn-sm" onClick={() => setOpen(true)}>
        Publish…
      </button>
      {open && <PublishDialog doc={doc} onClose={() => setOpen(false)} />}
    </div>
  )
}

/** For audio effects, which have no Export dialog. */
export function PublishAudioButton() {
  const doc = useDoc()
  const [open, setOpen] = useState(false)
  return (
    <>
      <button type="button" className="btn btn-primary btn-sm" onClick={() => setOpen(true)}>
        Publish
      </button>
      {open && <PublishDialog doc={doc} onClose={() => setOpen(false)} />}
    </>
  )
}
