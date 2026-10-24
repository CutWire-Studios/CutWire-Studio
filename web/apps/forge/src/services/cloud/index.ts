import type { ForgeDoc } from "@cutwire/forge-core/doc/types"
import { createEntry } from "@/services/storage/library"
import { SyncEngine } from "./engine"
import { useSync } from "./store"

export { SyncEngine, copyOf, type ConflictChoice, type EditorBridge, type LinkResult } from "./engine"
export { mergeLibrary, SYNC_LABEL, syncStateOf, useSync, useSyncState, type LibraryItem, type SyncState } from "./store"
export { stripBlobRefs } from "./assets"

async function encodeThumb(dataUrl: string): Promise<Blob | null> {
  const img = await createImageBitmap(await (await fetch(dataUrl)).blob())
  const c = document.createElement("canvas")
  c.width = img.width
  c.height = img.height
  c.getContext("2d")!.drawImage(img, 0, 0)
  for (const q of [0.8, 0.6, 0.4, 0.2]) {
    const blob = await new Promise<Blob | null>((r) => c.toBlob(r, "image/webp", q))
    if (blob && blob.type === "image/webp" && blob.size <= 64 * 1024) return blob
  }
  return null
}

export const sync = new SyncEngine({ encodeThumb })

/** Creates a library entry, and its cloud copy right away when signed in. */
export async function createSyncedEntry(doc: ForgeDoc, thumb?: string): Promise<string> {
  const id = await createEntry(doc, thumb)
  if (useSync.getState().signedIn) void sync.link(id)
  return id
}
