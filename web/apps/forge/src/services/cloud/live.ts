import { ApiError, forge, type ForgeDocRecord, type ForgeEvent } from "@cutwire/api-client"
import { loadEntry } from "@/services/storage/library"
import { hydrateAssets } from "./assets"
import type { SyncEngine } from "./engine"
import type { EventSourceLike } from "./types"
import { useSync } from "./store"

type Updated = Extract<ForgeEvent, { type: "updated" }>

const SOURCE_TEXT: Record<string, string> = {
  mcp: "Updated by MCP",
  ai: "Updated by the AI",
  web: "Updated from another tab or device",
}

/** Follows one cloud document's event stream for as long as the editor has it open. */
export function subscribeDoc(engine: SyncEngine, open: (url: string) => EventSourceLike, localId: string, cloudId: string): () => void {
  const es = open(forge.eventsUrl(cloudId))
  // The first open also checks for edits made since the library was last loaded; later opens are reconnects.
  es.onopen = () => void catchUp(engine, localId, cloudId)
  const handle = (raw: string, forced?: ForgeEvent["type"]) => {
    let ev: ForgeEvent
    try {
      const parsed = JSON.parse(raw || "{}") as Partial<ForgeEvent>
      ev = { ...parsed, type: forced ?? parsed.type } as ForgeEvent
    } catch {
      return
    }
    if (ev.type === "updated") void onRemoteUpdated(engine, localId, cloudId, ev)
    else if (ev.type === "deleted") engine.raiseDeleted(localId, engine.bridge.currentDoc(localId)?.meta.displayName ?? "This document")
  }
  es.addEventListener("updated", (e) => handle(e.data, "updated"))
  es.addEventListener("deleted", (e) => handle(e.data, "deleted"))
  es.addEventListener("message", (e) => handle(e.data))
  return () => es.close()
}

async function catchUp(engine: SyncEngine, localId: string, cloudId: string) {
  if (engine.hasUnsyncedEdits(localId)) return
  const known = useSync.getState().meta[localId]?.revision ?? (await loadEntry(localId))?.revision ?? 0
  try {
    const rec = await forge.get(cloudId)
    if (rec.revision > known) {
      const ev: Updated = { type: "updated", revision: rec.revision, updatedAt: rec.updatedAt, source: "web", clientId: null }
      await onRemoteUpdated(engine, localId, cloudId, ev, rec)
    }
  } catch (e) {
    if (e instanceof ApiError && e.status === 404) engine.raiseDeleted(localId, engine.bridge.currentDoc(localId)?.meta.displayName ?? "This document")
  }
}

async function flagConflict(engine: SyncEngine, localId: string, cloudId: string, fetched?: ForgeDocRecord) {
  try {
    const rec = fetched ?? (await forge.get(cloudId))
    const local = engine.bridge.currentDoc(localId) ?? (await loadEntry(localId))?.doc
    const doc = await hydrateAssets(rec.doc, local)
    engine.setConflict({ localId, name: local?.meta.displayName ?? rec.name, current: { revision: rec.revision, updatedAt: rec.updatedAt, doc } })
  } catch {
    // The next event or reconnect retries.
  }
}

/** An update from elsewhere (MCP, the AI, another tab): applied as one undo step, or a conflict if there are unsynced edits. */
export async function onRemoteUpdated(engine: SyncEngine, localId: string, cloudId: string, ev: Updated, fetched?: ForgeDocRecord) {
  if (ev.clientId && ev.clientId === engine.clientId) return
  const known = useSync.getState().meta[localId]?.revision ?? (await loadEntry(localId))?.revision ?? 0
  if (ev.revision <= known) return
  if (engine.hasUnsyncedEdits(localId)) return flagConflict(engine, localId, cloudId, fetched)
  const rec = fetched ?? (await forge.get(cloudId).catch(() => null))
  if (!rec) return
  const entry = await loadEntry(localId)
  const doc = await hydrateAssets(rec.doc, engine.bridge.currentDoc(localId) ?? entry?.doc)
  await engine.adoptRemote(localId, doc, rec.revision, rec.updatedAt)
  engine.notify(SOURCE_TEXT[ev.source] ?? SOURCE_TEXT.web)
}
