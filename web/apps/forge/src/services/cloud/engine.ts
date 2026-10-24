import { ApiError, DocumentExistsError, forge, RevisionConflictError, type ForgeConflict, type ForgeDocRecord } from "@cutwire/api-client"
import type { ForgeDoc } from "@cutwire/forge-core/doc/types"
import { createEntry, deleteEntry, listLibrary, loadEntry, patchEntry, type LibraryEntry } from "@/services/storage/library"
import { hydrateAssets, toCloudPayload, uploadAssets } from "./assets"
import { docHash } from "./hash"
import type { ConflictChoice, EditorBridge, EngineOptions, EventSourceLike, LinkResult } from "./types"
import { copyOf, isTransient, sameAssets, thumbMark } from "./util"
import { hydrate, hydrateByRoute, reconcile } from "./reconcile"
import { subscribeDoc } from "./live"
import { initialSyncStore, metaOf, useSync, type Conflict, type Runtime } from "./store"

const noBridge: EditorBridge = { isOpen: () => false, currentDoc: () => null, apply: () => {} }
const MAX_BACKOFF_MS = 60_000
const THUMB_EVERY_MS = 30_000

export type { ConflictChoice, EditorBridge, EngineOptions, EventSourceLike, LinkResult } from "./types"
export { copyOf } from "./util"

export class SyncEngine {
  readonly clientId: string
  bridge: EditorBridge
  private debounceMs: number
  private timers = new Map<string, ReturnType<typeof setTimeout>>()
  private retries = new Map<string, ReturnType<typeof setTimeout>>()
  private attempts = new Map<string, number>()
  private inflight = new Map<string, Promise<void>>()
  private again = new Set<string>()
  private lastThumb = new Map<string, number>()

  constructor(private opts: EngineOptions = {}) {
    this.clientId = opts.clientId ?? crypto.randomUUID()
    this.debounceMs = opts.debounceMs ?? 1500
    this.bridge = opts.bridge ?? noBridge
    this.notifier = opts.notify
  }

  /** Where user-facing messages go; the app points this at its toasts. */
  notifier: ((text: string, kind?: "info" | "error") => void) | undefined

  notify(text: string, kind: "info" | "error" = "info") {
    this.notifier?.(text, kind)
  }

  // ---- store helpers -------------------------------------------------------------------------

  private setRuntime(localId: string, r: Runtime) {
    useSync.setState((s) => ({ runtime: { ...s.runtime, [localId]: r } }))
  }

  publish(e: LibraryEntry) {
    useSync.setState((s) => ({ meta: { ...s.meta, [e.localId]: metaOf(e) } }))
  }

  setConflict(c: Conflict) {
    this.clearTimers(c.localId)
    useSync.setState((s) => ({
      conflicts: { ...s.conflicts, [c.localId]: c },
      activeConflict: c.localId,
      runtime: { ...s.runtime, [c.localId]: { phase: "conflict" } },
    }))
  }

  private clearConflict(localId: string) {
    useSync.setState((s) => {
      const conflicts = { ...s.conflicts }
      delete conflicts[localId]
      return {
        conflicts,
        activeConflict: s.activeConflict === localId ? null : s.activeConflict,
        runtime: { ...s.runtime, [localId]: { phase: "idle" } },
      }
    })
  }

  private clearTimers(localId: string) {
    clearTimeout(this.timers.get(localId))
    clearTimeout(this.retries.get(localId))
    this.timers.delete(localId)
    this.retries.delete(localId)
  }

  /** Called after every local save: tells the store, and starts the push countdown for linked, changed documents. */
  noteSaved(e: LibraryEntry) {
    this.publish(e)
    if (e.cloudId && e.dirty) this.schedulePush(e.localId)
  }

  /** Reads one entry's sync fields into the store. */
  async loadMeta(localId: string): Promise<LibraryEntry | undefined> {
    const e = await loadEntry(localId)
    if (e) this.publish(e)
    return e
  }

  // ---- session -------------------------------------------------------------------------------

  setSignedIn(signedIn: boolean) {
    if (useSync.getState().signedIn === signedIn) return
    useSync.setState({ signedIn })
    if (signedIn) void this.refresh()
    else {
      for (const id of [...this.timers.keys(), ...this.retries.keys()]) this.clearTimers(id)
      useSync.setState({ cloud: null, runtime: {}, conflicts: {}, activeConflict: null })
    }
  }

  setOnline(online: boolean) {
    useSync.setState({ online })
    if (online) void this.retryAll()
  }

  // ---- pushing -------------------------------------------------------------------------------

  /** Pushes `delay` ms from now; a newer edit moves the deadline. */
  schedulePush(localId: string, delay = this.debounceMs) {
    if (!useSync.getState().signedIn) return
    clearTimeout(this.timers.get(localId))
    this.timers.set(
      localId,
      setTimeout(() => {
        this.timers.delete(localId)
        void this.push(localId)
      }, delay),
    )
  }

  /** Pushes everything that is waiting, now (the tab is being hidden). */
  async flush() {
    const waiting = [...this.timers.keys()]
    for (const id of waiting) this.clearTimers(id)
    await Promise.all(waiting.map((id) => this.push(id)))
  }

  private async retryAll() {
    const ids = Object.entries(useSync.getState().runtime)
      .filter(([, r]) => r.phase === "offline")
      .map(([id]) => id)
    for (const id of ids) this.clearTimers(id)
    this.attempts.clear()
    await Promise.all(ids.map((id) => this.push(id)))
  }

  push(localId: string): Promise<void> {
    const running = this.inflight.get(localId)
    if (running) {
      this.again.add(localId)
      return running
    }
    const p = this.doPush(localId).finally(() => {
      this.inflight.delete(localId)
      if (this.again.delete(localId)) this.schedulePush(localId, 0)
    })
    this.inflight.set(localId, p)
    return p
  }

  private async doPush(localId: string) {
    const st = useSync.getState()
    if (!st.signedIn || st.conflicts[localId]) return
    this.clearTimers(localId)
    const entry = await loadEntry(localId)
    if (!entry?.cloudId) return
    const doc = this.bridge.currentDoc(localId) ?? entry.doc
    const hash = docHash(doc)
    if (hash === entry.syncedHash) {
      if (entry.dirty) this.publish((await patchEntry(localId, (e) => ({ ...e, dirty: false }))) ?? entry)
      this.setRuntime(localId, { phase: "idle" })
      return
    }
    this.setRuntime(localId, { phase: "syncing" })
    try {
      const withBlobs = await uploadAssets(doc)
      const res = await forge.update(entry.cloudId, toCloudPayload(withBlobs), entry.revision ?? 0, this.clientId)
      const saved = await patchEntry(localId, (e) => ({
        ...e,
        doc: sameAssets(e.doc, withBlobs) ? { ...e.doc, assets: withBlobs.assets } : e.doc,
        revision: res.revision,
        syncedAt: Date.now(),
        syncedHash: hash,
        dirty: docHash(e.doc) !== hash,
      }))
      this.attempts.delete(localId)
      this.setRuntime(localId, { phase: "idle" })
      useSync.setState({ lastSyncedAt: Date.now() })
      if (saved) {
        this.publish(saved)
        if (saved.dirty) this.schedulePush(localId, 0)
        else void this.pushThumb(saved)
      }
    } catch (e) {
      await this.pushFailed(localId, entry, e)
    }
  }

  private async pushFailed(localId: string, entry: LibraryEntry, e: unknown) {
    if (e instanceof RevisionConflictError) {
      this.setConflict({ localId, name: entry.doc.meta.displayName, current: e.current as ForgeConflict<unknown> })
    } else if (e instanceof ApiError && e.status === 401) {
      this.setRuntime(localId, { phase: "idle" })
      useSync.setState({ signedIn: false })
    } else if (e instanceof ApiError && e.status === 404) {
      this.setRuntime(localId, { phase: "idle" })
      await this.unlink(localId)
      this.notify(`"${entry.doc.meta.displayName}" is no longer in your cloud library. Kept as a local copy.`)
    } else if (isTransient(e)) {
      const n = (this.attempts.get(localId) ?? 0) + 1
      this.attempts.set(localId, n)
      this.setRuntime(localId, { phase: "offline" })
      this.retries.set(
        localId,
        setTimeout(() => {
          this.retries.delete(localId)
          void this.push(localId)
        }, Math.min(MAX_BACKOFF_MS, 2000 * 2 ** (n - 1))),
      )
    } else {
      this.setRuntime(localId, { phase: "error", message: e instanceof Error ? e.message : "Sync failed." })
      this.notify(`Couldn't sync "${entry.doc.meta.displayName}": ${e instanceof Error ? e.message : "unknown error"}`, "error")
    }
  }

  private async pushThumb(entry: LibraryEntry) {
    const encode = this.opts.encodeThumb
    if (!encode || !entry.thumb || !entry.cloudId || thumbMark(entry.thumb) === entry.thumbSynced) return
    if (Date.now() - (this.lastThumb.get(entry.localId) ?? 0) < THUMB_EVERY_MS) return
    this.lastThumb.set(entry.localId, Date.now())
    try {
      const blob = await encode(entry.thumb)
      if (!blob) return
      await forge.putThumb(entry.cloudId, blob)
      await patchEntry(entry.localId, (e) => ({ ...e, thumbSynced: thumbMark(entry.thumb!) }))
    } catch {
      // The thumbnail is a nicety; the next push tries again.
    }
  }

  // ---- linking -------------------------------------------------------------------------------

  /** Creates the cloud copy of a local entry. */
  async link(localId: string): Promise<LinkResult> {
    const entry = await loadEntry(localId)
    if (!entry || entry.cloudId) return "failed"
    const doc = this.bridge.currentDoc(localId) ?? entry.doc
    const hash = docHash(doc)
    try {
      const withBlobs = await uploadAssets(doc)
      const rec = await forge.create(localId, toCloudPayload(withBlobs))
      await this.afterLink(localId, rec.id, rec.revision, hash, withBlobs)
      return "created"
    } catch (e) {
      if (e instanceof DocumentExistsError) return this.adopt(localId, e.id, doc)
      if (e instanceof ApiError && e.status === 401) useSync.setState({ signedIn: false })
      return "failed"
    }
  }

  private async afterLink(localId: string, cloudId: string, revision: number, hash: string, withBlobs: ForgeDoc) {
    const saved = await patchEntry(localId, (e) => ({
      ...e,
      doc: sameAssets(e.doc, withBlobs) ? { ...e.doc, assets: withBlobs.assets } : e.doc,
      cloudId,
      revision,
      syncedAt: Date.now(),
      syncedHash: hash,
      dirty: docHash(e.doc) !== hash,
    }))
    if (saved) {
      this.publish(saved)
      useSync.setState({ lastSyncedAt: Date.now() })
      if (saved.dirty) this.schedulePush(localId)
    }
    await this.refreshList()
  }

  /** A document with this localId already lives in the cloud: link to it, or flag a conflict if they differ. */
  async adopt(localId: string, cloudId: string, mine: ForgeDoc): Promise<LinkResult> {
    try {
      const rec = await forge.get(cloudId)
      const theirs = await hydrateAssets(rec.doc, mine)
      const same = docHash(theirs) === docHash(mine)
      const saved = await patchEntry(localId, (e) => ({
        ...e,
        cloudId,
        revision: rec.revision,
        syncedAt: Date.now(),
        syncedHash: same ? docHash(mine) : undefined,
        dirty: !same,
      }))
      if (saved) this.publish(saved)
      if (same) return "adopted"
      this.setConflict({ localId, name: mine.meta.displayName, current: { revision: rec.revision, updatedAt: rec.updatedAt, doc: theirs } })
      return "conflict"
    } catch {
      return "failed"
    }
  }

  /** Uploads every entry that has no cloud copy (the "Upload local library" action). */
  async uploadLocalLibrary(): Promise<Record<LinkResult, number>> {
    const out: Record<LinkResult, number> = { created: 0, adopted: 0, conflict: 0, failed: 0 }
    for (const e of await listLibrary()) {
      if (e.cloudId) continue
      out[await this.link(e.localId)]++
    }
    return out
  }

  async unlink(localId: string) {
    this.clearTimers(localId)
    const saved = await patchEntry(localId, (e) => {
      const { cloudId: _c, revision: _r, syncedAt: _s, syncedHash: _h, thumbSynced: _t, ...rest } = e
      return { ...rest, dirty: false }
    })
    if (saved) this.publish(saved)
    this.clearConflict(localId)
    await this.refreshList()
  }

  // ---- pulling -------------------------------------------------------------------------------

  async refreshList() {
    if (!useSync.getState().signedIn) return
    try {
      useSync.setState({ cloud: await forge.list() })
    } catch (e) {
      if (e instanceof ApiError && e.status === 401) useSync.setState({ signedIn: false })
    }
  }

  refresh = () => reconcile(this)
  hydrate = (cloudId: string) => hydrate(this, cloudId)
  hydrateByRoute = (route: string) => hydrateByRoute(this, route)
  subscribe = (localId: string, cloudId: string) =>
    subscribeDoc(this, this.opts.openEvents ?? ((url) => new EventSource(url, { withCredentials: true }) as unknown as EventSourceLike), localId, cloudId)

  /** Fetches the cloud's copy and replaces the local one (and the open editor's, as one undo step). */
  async pull(localId: string, cloudId: string): Promise<ForgeDocRecord | null> {
    let rec: ForgeDocRecord
    try {
      rec = await forge.get(cloudId)
    } catch (e) {
      if (e instanceof ApiError && e.status === 401) useSync.setState({ signedIn: false })
      return null
    }
    const entry = await loadEntry(localId)
    const doc = await hydrateAssets(rec.doc, this.bridge.currentDoc(localId) ?? entry?.doc)
    await this.adoptRemote(localId, doc, rec.revision, rec.updatedAt)
    return rec
  }

  async adoptRemote(localId: string, doc: ForgeDoc, revision: number, updatedAt?: string) {
    const saved = await patchEntry(localId, (e) => ({
      ...e,
      doc,
      updatedAt: (updatedAt && Date.parse(updatedAt)) || Date.now(),
      revision,
      syncedAt: Date.now(),
      syncedHash: docHash(doc),
      dirty: false,
    }))
    if (saved) this.publish(saved)
    this.setRuntime(localId, { phase: "idle" })
    if (this.bridge.isOpen(localId)) this.bridge.apply(localId, doc)
    useSync.setState({ lastSyncedAt: Date.now() })
  }

  /** The cloud no longer has the document this entry is linked to. */
  async cloudCopyGone(e: LibraryEntry) {
    const name = e.doc.meta.displayName
    if (this.bridge.isOpen(e.localId)) this.raiseDeleted(e.localId, name)
    else if (!e.dirty) {
      await this.unlink(e.localId)
      this.notify(`"${name}" was deleted from the cloud. Kept as a local copy.`)
    }
  }

  hasUnsyncedEdits(localId: string): boolean {
    const s = useSync.getState()
    const m = s.meta[localId]
    if (s.runtime[localId]?.phase === "syncing" || this.inflight.has(localId) || this.timers.has(localId)) return true
    if (m?.dirty) return true
    const open = this.bridge.currentDoc(localId)
    return !!open && !!m?.syncedHash && docHash(open) !== m.syncedHash
  }

  raiseDeleted(localId: string, name: string) {
    this.clearTimers(localId)
    useSync.setState({ deleted: { localId, name } })
  }

  async keepAsLocalCopy(localId: string) {
    const doc = this.bridge.currentDoc(localId)
    if (doc) await patchEntry(localId, (e) => ({ ...e, doc }))
    await this.unlink(localId)
    useSync.setState({ deleted: null })
  }

  async discardAfterRemoteDelete(localId: string) {
    this.clearTimers(localId)
    await deleteEntry(localId)
    useSync.setState((s) => {
      const meta = { ...s.meta }
      delete meta[localId]
      return { deleted: null, meta }
    })
    await this.refreshList()
  }

  // ---- conflicts -----------------------------------------------------------------------------

  async resolve(localId: string, choice: ConflictChoice) {
    const c = useSync.getState().conflicts[localId]
    const entry = await loadEntry(localId)
    if (!c || !entry) return
    const mine = this.bridge.currentDoc(localId) ?? entry.doc
    if (choice === "mine") {
      const saved = await patchEntry(localId, (e) => ({ ...e, doc: mine, revision: c.current.revision, syncedHash: undefined, dirty: true }))
      if (saved) this.publish(saved)
      this.clearConflict(localId)
      await this.push(localId)
      return
    }
    if (choice === "both") {
      const copyId = await createEntry(copyOf(mine, `${mine.meta.displayName} (copy)`), entry.thumb)
      void this.link(copyId).then(() => this.refreshList())
      this.notify(`Your version was saved as "${mine.meta.displayName} (copy)".`)
    }
    const theirs = await hydrateAssets(c.current.doc, mine)
    this.clearConflict(localId)
    await this.adoptRemote(localId, theirs, c.current.revision, c.current.updatedAt)
  }

  dismissConflict() {
    useSync.setState({ activeConflict: null })
  }

  openConflict(localId: string) {
    useSync.setState({ activeConflict: localId })
  }

  // ---- deleting ------------------------------------------------------------------------------

  /** Removes the cloud copy; the local one stays, as a local-only document. */
  async deleteFromCloud(cloudId: string, localId?: string) {
    await forge.remove(cloudId)
    if (localId) await this.unlink(localId)
    else await this.refreshList()
  }

  /** Removes the cloud copy and the local one. */
  async deleteEverywhere(localId: string, cloudId?: string) {
    if (cloudId) await forge.remove(cloudId)
    this.clearTimers(localId)
    await deleteEntry(localId)
    useSync.setState((s) => {
      const meta = { ...s.meta }
      delete meta[localId]
      return { meta }
    })
    await this.refreshList()
  }
}

export function resetSyncStore() {
  useSync.setState({ ...initialSyncStore, meta: {}, runtime: {}, conflicts: {} }, true)
}
