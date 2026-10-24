import { sync, useSync } from "@/services/cloud"
import { Modal } from "@/shared/ui/Modal"
import { ago } from "./ago"
import "./sync.css"

export function SyncDialogs() {
  return (
    <>
      <ConflictDialog />
      <DeletedDialog />
    </>
  )
}

function ConflictDialog() {
  const id = useSync((s) => s.activeConflict)
  const conflict = useSync((s) => (id ? s.conflicts[id] : undefined))
  const mineAt = useSync((s) => (id ? s.meta[id]?.syncedAt : undefined))
  if (!id || !conflict) return null
  return (
    <Modal title="This document changed in the cloud" onClose={() => sync.dismissConflict()}>
      <div className="stack gap-3">
        <p>
          <strong>{conflict.name}</strong> was changed somewhere else (another tab, MCP or the AI) while you also had edits that
          haven't synced yet. Choose which version to keep.
        </p>
        <p className="meta small">
          Cloud version: revision {conflict.current.revision}, saved {ago(Date.parse(conflict.current.updatedAt) || 0)}.
          {mineAt ? ` You last synced ${ago(mineAt)}.` : ""}
        </p>
        <div className="conflict-actions">
          <button type="button" className="btn btn-primary btn-sm" onClick={() => void sync.resolve(id, "mine")}>
            Keep mine (overwrite)
          </button>
          <button type="button" className="btn btn-secondary btn-sm" onClick={() => void sync.resolve(id, "server")}>
            Take server version
          </button>
          <button type="button" className="btn btn-secondary btn-sm" onClick={() => void sync.resolve(id, "both")}>
            Keep both (save mine as a copy)
          </button>
        </div>
        <p className="meta small">Taking the server version is one undo step in the editor.</p>
      </div>
    </Modal>
  )
}

function DeletedDialog() {
  const deleted = useSync((s) => s.deleted)
  if (!deleted) return null
  return (
    <Modal title="Deleted from your cloud library" onClose={() => useSync.setState({ deleted: null })}>
      <div className="stack gap-3">
        <p>
          <strong>{deleted.name}</strong> was deleted from your cloud library. It still exists in this browser.
        </p>
        <div className="conflict-actions">
          <button type="button" className="btn btn-primary btn-sm" onClick={() => void sync.keepAsLocalCopy(deleted.localId)}>
            Keep as local copy
          </button>
          <button
            type="button"
            className="btn btn-secondary btn-sm danger-text"
            onClick={() => void sync.discardAfterRemoteDelete(deleted.localId).then(() => location.assign(import.meta.env.BASE_URL))}
          >
            Delete here too
          </button>
        </div>
      </div>
    </Modal>
  )
}
