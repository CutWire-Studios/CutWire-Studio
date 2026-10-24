import { forge, type ForgeDocSummary } from "@cutwire/api-client"
import { listLibrary, saveEntry, type LibraryEntry } from "@/services/storage/library"
import { hydrateAssets } from "./assets"
import type { SyncEngine } from "./engine"
import { docHash } from "./hash"
import { useSync } from "./store"

/** The cloud document an entry corresponds to: by cloud id, else (if unlinked) by localId. */
function counterpart(e: LibraryEntry, cloud: ForgeDocSummary[]): ForgeDocSummary | undefined {
  if (e.cloudId) return cloud.find((d) => d.id === e.cloudId)
  return cloud.find((d) => d.localId === e.localId)
}

async function reconcileOne(engine: SyncEngine, e: LibraryEntry, cloud: ForgeDocSummary[]): Promise<string | null> {
  const c = counterpart(e, cloud)
  if (!e.cloudId) {
    if (c) await engine.adopt(e.localId, c.id, e.doc)
    return c?.id ?? null
  }
  if (!c) {
    await engine.cloudCopyGone(e)
    return null
  }
  if (c.revision > (e.revision ?? 0)) {
    if (engine.hasUnsyncedEdits(e.localId)) engine.schedulePush(e.localId, 0)
    else await engine.pull(e.localId, c.id)
  } else if (e.dirty) engine.schedulePush(e.localId, 0)
  return c.id
}

/** Reconciles this browser's library with the cloud: pulls newer copies, hydrates cloud-only docs. */
export async function reconcile(engine: SyncEngine) {
  if (!useSync.getState().signedIn) return
  await engine.refreshList()
  const cloud = useSync.getState().cloud
  if (!cloud) return
  const locals = await listLibrary()
  for (const e of locals) engine.publish(e)
  const known = new Set<string>()
  for (const e of locals) {
    const id = await reconcileOne(engine, e, cloud)
    if (id) known.add(id)
  }
  for (const c of cloud) if (!known.has(c.id)) await hydrate(engine, c.id).catch(() => {})
}

/** Saves a cloud-only document into this browser; returns its localId. */
export async function hydrate(engine: SyncEngine, cloudId: string): Promise<string> {
  const rec = await forge.get(cloudId)
  const doc = await hydrateAssets(rec.doc)
  const localId = rec.localId || rec.id
  const entry: LibraryEntry = {
    localId,
    doc,
    updatedAt: Date.parse(rec.updatedAt) || Date.now(),
    cloudId: rec.id,
    revision: rec.revision,
    syncedAt: Date.now(),
    syncedHash: docHash(doc),
    dirty: false,
  }
  await saveEntry(entry)
  engine.publish(entry)
  return localId
}

/** Opens `route` (a localId or cloud id) from the cloud when this browser doesn't have it. */
export async function hydrateByRoute(engine: SyncEngine, route: string): Promise<string | null> {
  if (!useSync.getState().signedIn) return null
  if (!useSync.getState().cloud) await engine.refreshList()
  const c = useSync.getState().cloud?.find((d) => d.localId === route || d.id === route)
  if (!c) return null
  try {
    return await hydrate(engine, c.id)
  } catch {
    return null
  }
}
