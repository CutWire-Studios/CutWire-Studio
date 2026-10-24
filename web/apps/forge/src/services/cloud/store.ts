import type { ForgeConflict, ForgeDocSummary } from "@cutwire/api-client"
import { create } from "zustand"
import type { LibraryEntry } from "@/services/storage/library"

export type SyncPhase = "idle" | "syncing" | "offline" | "conflict" | "error"

/** What the UI shows for a document. */
export type SyncState = "local" | "synced" | "pending" | "syncing" | "offline" | "conflict" | "error"

export interface EntryMeta {
  cloudId?: string
  revision?: number
  syncedAt?: number
  dirty: boolean
  syncedHash?: string
}

export interface Runtime {
  phase: SyncPhase
  message?: string
}

export interface Conflict {
  localId: string
  name: string
  current: ForgeConflict<unknown>
}

export interface SyncStore {
  signedIn: boolean
  online: boolean
  /** the cloud's document list as of the last refresh; null until loaded */
  cloud: ForgeDocSummary[] | null
  meta: Record<string, EntryMeta>
  runtime: Record<string, Runtime>
  conflicts: Record<string, Conflict>
  /** the conflict whose dialog is open */
  activeConflict: string | null
  /** documents removed from the cloud while open, waiting for the user's choice */
  deleted: { localId: string; name: string } | null
  lastSyncedAt: number | null
}

export const initialSyncStore: SyncStore = {
  signedIn: false,
  online: true,
  cloud: null,
  meta: {},
  runtime: {},
  conflicts: {},
  activeConflict: null,
  deleted: null,
  lastSyncedAt: null,
}

export const useSync = create<SyncStore>(() => initialSyncStore)

export function metaOf(e: LibraryEntry): EntryMeta {
  return { cloudId: e.cloudId, revision: e.revision, syncedAt: e.syncedAt, dirty: !!e.dirty, syncedHash: e.syncedHash }
}

export function syncStateOf(meta: EntryMeta | undefined, runtime: Runtime | undefined, signedIn: boolean): SyncState {
  if (!signedIn || !meta?.cloudId) return "local"
  switch (runtime?.phase) {
    case "conflict":
      return "conflict"
    case "syncing":
      return "syncing"
    case "offline":
      return "offline"
    case "error":
      return "error"
  }
  return meta.dirty ? "pending" : "synced"
}

export const SYNC_LABEL: Record<SyncState, string> = {
  local: "Local only",
  synced: "Synced",
  pending: "Waiting to sync",
  syncing: "Syncing…",
  offline: "Offline, will retry",
  conflict: "Conflict",
  error: "Sync failed",
}

export function useSyncState(localId: string): SyncState {
  return useSync((s) => syncStateOf(s.meta[localId], s.runtime[localId], s.signedIn))
}

export type LibraryItem =
  | { kind: "local"; localId: string; entry: LibraryEntry; cloud?: ForgeDocSummary }
  | { kind: "cloud"; localId: string; cloud: ForgeDocSummary }

/** The library as the union of this browser's entries and the cloud's documents, linked by localId. */
export function mergeLibrary(local: LibraryEntry[], cloud: ForgeDocSummary[] | null): LibraryItem[] {
  const byLocal = new Map((cloud ?? []).map((c) => [c.localId, c]))
  const byCloud = new Map((cloud ?? []).map((c) => [c.id, c]))
  const used = new Set<string>()
  const items: LibraryItem[] = local.map((entry) => {
    const c = (entry.cloudId ? byCloud.get(entry.cloudId) : undefined) ?? byLocal.get(entry.localId)
    if (c) used.add(c.id)
    return { kind: "local", localId: entry.localId, entry, cloud: c }
  })
  for (const c of cloud ?? []) if (!used.has(c.id)) items.push({ kind: "cloud", localId: c.localId || c.id, cloud: c })
  return items.sort((a, b) => stamp(b) - stamp(a))
}

const stamp = (i: LibraryItem) => (i.kind === "local" ? i.entry.updatedAt : Date.parse(i.cloud.updatedAt) || 0)
