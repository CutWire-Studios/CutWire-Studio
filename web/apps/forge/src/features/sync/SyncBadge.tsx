import { SYNC_LABEL, useSync, useSyncState, type SyncState } from "@/services/cloud"
import { ago } from "./ago"
import "./sync.css"

export function SyncBadge({ state, title }: { state: SyncState; title?: string }) {
  return (
    <span className={`sync-badge sync-${state}`} title={title ?? SYNC_LABEL[state]}>
      <span className="sync-dot" aria-hidden="true" />
      {SYNC_LABEL[state]}
    </span>
  )
}

/** The editor's save state for one document. */
export function DocSyncStatus({ localId }: { localId: string }) {
  const state = useSyncState(localId)
  const syncedAt = useSync((s) => s.meta[localId]?.syncedAt)
  if (state === "local") return null
  return <SyncBadge state={state} title={syncedAt ? `Last synced ${ago(syncedAt)}` : SYNC_LABEL[state]} />
}

/** Header summary: signed-out hint, offline, conflicts, in-flight syncs, or the last sync time. */
export function SyncIndicator() {
  const signedIn = useSync((s) => s.signedIn)
  const online = useSync((s) => s.online)
  const lastSyncedAt = useSync((s) => s.lastSyncedAt)
  const busy = useSync((s) => Object.values(s.runtime).filter((r) => r.phase === "syncing").length + Object.values(s.meta).filter((m) => m.dirty).length)
  const conflicts = useSync((s) => Object.keys(s.conflicts).length)
  const offline = useSync((s) => Object.values(s.runtime).some((r) => r.phase === "offline"))

  let state: SyncState
  let label: string
  if (!signedIn) return <span className="meta small sync-indicator">Local only</span>
  if (conflicts) [state, label] = ["conflict", `${conflicts} conflict${conflicts > 1 ? "s" : ""}`]
  else if (!online || offline) [state, label] = ["offline", "Offline, will retry"]
  else if (busy) [state, label] = ["syncing", "Syncing…"]
  else [state, label] = ["synced", lastSyncedAt ? `Synced ${ago(lastSyncedAt)}` : "Cloud library"]
  return (
    <span className={`sync-badge sync-${state} sync-indicator`} title="Cloud sync status">
      <span className="sync-dot" aria-hidden="true" />
      {label}
    </span>
  )
}
