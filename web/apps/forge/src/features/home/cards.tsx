import { useEffect, useState } from "react"
import { Link } from "react-router"
import { KIND_INFO } from "@cutwire/forge-core/doc/kinds"
import type { ForgeDoc } from "@cutwire/forge-core/doc/types"
import type { Starter } from "@cutwire/forge-core/starters"
import { renderCardThumb } from "@/services/preview/cardThumb"
import type { ForgeDocSummary } from "@cutwire/api-client"
import { sync, useSync, useSyncState } from "@/services/cloud"
import type { LibraryEntry } from "@/services/storage/library"
import { ago, SyncBadge } from "@/features/sync"

function useCardThumb(doc: ForgeDoc, stored?: string) {
  const [src, setSrc] = useState(stored ?? null)
  useEffect(() => {
    // Audio effects have no picture to render.
    if (stored || doc.kind === "audio") return
    let live = true
    renderCardThumb(doc)
      .then((url) => live && setSrc(url))
      .catch(() => {})
    return () => {
      live = false
    }
  }, [doc, stored])
  return src
}

export function StarterCard({ starter, onPick }: { starter: Starter; onPick: () => void }) {
  const thumb = useCardThumb(starter.doc)
  return (
    <button type="button" className="card starter-card" onClick={onPick}>
      <div className="card-thumb">{thumb ? <img src={thumb} alt="" /> : <div className="skeleton" />}</div>
      <div className="card-text">
        <strong>{starter.name}</strong>
        <span className="meta">{starter.description}</span>
      </div>
    </button>
  )
}

function CardThumb({ doc, thumb }: { doc: ForgeDoc; thumb: string | null }) {
  if (doc.kind === "audio") {
    return (
      <div className="audio-thumb" aria-hidden="true">
        ♪
      </div>
    )
  }
  if (thumb) return <img src={thumb} alt="" />
  return <div className="skeleton" />
}

interface CardActions {
  onDelete: () => void
  onDeleteFromCloud: () => void
  onDeleteEverywhere: () => void
}

function DeleteButtons({ linked, onDelete, onDeleteFromCloud, onDeleteEverywhere }: CardActions & { linked: boolean }) {
  if (!linked) {
    return (
      <button type="button" className="btn btn-tertiary btn-sm danger-text" onClick={onDelete}>
        Delete
      </button>
    )
  }
  return (
    <>
      <button type="button" className="btn btn-tertiary btn-sm" title="Removes the cloud copy; keeps this browser's copy" onClick={onDeleteFromCloud}>
        Delete from cloud
      </button>
      <button type="button" className="btn btn-tertiary btn-sm danger-text" onClick={onDeleteEverywhere}>
        Delete everywhere
      </button>
    </>
  )
}

export function LibraryCard({ entry, onDuplicate, ...del }: { entry: LibraryEntry; onDuplicate: () => void } & CardActions) {
  const thumb = useCardThumb(entry.doc, entry.thumb)
  const state = useSyncState(entry.localId)
  const signedIn = useSync((s) => s.signedIn)
  const syncedAt = useSync((s) => s.meta[entry.localId]?.syncedAt ?? entry.syncedAt)
  const linked = signedIn && !!entry.cloudId
  return (
    <div className="card lib-card">
      <Link to={`/edit/${entry.localId}`} className="card-thumb">
        <CardThumb doc={entry.doc} thumb={thumb} />
        <span className="pill kind-pill">{KIND_INFO[entry.doc.kind].label}</span>
      </Link>
      <div className="card-text">
        <Link to={`/edit/${entry.localId}`}>
          <strong>{entry.doc.meta.displayName}</strong>
        </Link>
        <span className="meta">
          Edited {ago(entry.updatedAt)}
          {linked && syncedAt ? ` · synced ${ago(syncedAt)}` : ""}
        </span>
        <span className="card-sync">
          <SyncBadge state={state} />
          {state === "conflict" && (
            <button type="button" className="link-btn" onClick={() => sync.openConflict(entry.localId)}>
              Resolve
            </button>
          )}
        </span>
      </div>
      <div className="card-actions">
        <button type="button" className="btn btn-tertiary btn-sm" onClick={onDuplicate}>
          Duplicate
        </button>
        <DeleteButtons linked={linked} {...del} />
      </div>
    </div>
  )
}

/** A document that exists in the cloud and hasn't been downloaded to this browser yet. */
export function CloudCard({ cloud, onDeleteFromCloud }: { cloud: ForgeDocSummary; onDeleteFromCloud: () => void }) {
  const to = `/edit/${cloud.localId || cloud.id}`
  return (
    <div className="card lib-card">
      <Link to={to} className="card-thumb">
        {cloud.thumbUrl ? <img src={cloud.thumbUrl} alt="" /> : <div className="audio-thumb" aria-hidden="true">{cloud.kind === "audio" ? "♪" : "☁"}</div>}
        <span className="pill kind-pill">Cloud</span>
      </Link>
      <div className="card-text">
        <Link to={to}>
          <strong>{cloud.name}</strong>
        </Link>
        <span className="meta">Updated {ago(Date.parse(cloud.updatedAt) || 0)}</span>
        <span className="card-sync">
          <SyncBadge state="synced" title="In your cloud library; opens and downloads here" />
        </span>
      </div>
      <div className="card-actions">
        <button type="button" className="btn btn-tertiary btn-sm danger-text" onClick={onDeleteFromCloud}>
          Delete from cloud
        </button>
      </div>
    </div>
  )
}
