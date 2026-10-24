import { createStore, del, entries, get, set } from "idb-keyval"
import type { ForgeDoc } from "@cutwire/forge-core/doc/types"
import { uid } from "@cutwire/forge-core/doc/util"
import { parseForgeDoc } from "@cutwire/forge-core/export/link"
import { docHash } from "@/services/cloud/hash"

export interface LibraryEntry {
  localId: string
  doc: ForgeDoc
  updatedAt: number
  /** small JPEG data URL of the last preview frame */
  thumb?: string
  /** id of the linked cloud document; absent for local-only entries */
  cloudId?: string
  /** the cloud revision this entry was last in sync with */
  revision?: number
  /** epoch ms of the last successful sync */
  syncedAt?: number
  /** the entry has edits the cloud doesn't have yet */
  dirty?: boolean
  /** docHash of the document as the cloud holds it at `revision` */
  syncedHash?: string
  /** marker of the thumbnail last uploaded to the cloud */
  thumbSynced?: string
}

const store = createStore("drift-forge", "library")

export async function listLibrary(): Promise<LibraryEntry[]> {
  const all = (await entries<string, LibraryEntry>(store)).map(([, v]) => v)
  return all.sort((a, b) => b.updatedAt - a.updatedAt)
}

export async function loadEntry(localId: string): Promise<LibraryEntry | undefined> {
  const e = await get<LibraryEntry>(localId, store)
  if (e) e.doc = parseForgeDoc(JSON.stringify(e.doc))
  return e
}

export async function saveEntry(entry: LibraryEntry): Promise<void> {
  await set(entry.localId, entry, store)
}

/** Atomically rewrites one entry (one transaction); a missing entry stays missing. */
export async function patchEntry(localId: string, fn: (e: LibraryEntry) => LibraryEntry | undefined): Promise<LibraryEntry | undefined> {
  return store("readwrite", (os) => {
    return new Promise<LibraryEntry | undefined>((resolve, reject) => {
      const read = os.get(localId)
      read.onerror = () => reject(read.error)
      read.onsuccess = () => {
        const cur = read.result as LibraryEntry | undefined
        if (!cur) return resolve(undefined)
        const next = fn(cur) ?? cur
        const write = os.put(next, localId)
        write.onerror = () => reject(write.error)
        write.onsuccess = () => resolve(next)
      }
    })
  })
}

/** An editor autosave: keeps the cloud link, and flags the entry dirty if the cloud copy differs. */
export async function saveDoc(localId: string, doc: ForgeDoc, thumb?: string): Promise<LibraryEntry> {
  return store("readwrite", (os) => {
    return new Promise<LibraryEntry>((resolve, reject) => {
      const read = os.get(localId)
      read.onerror = () => reject(read.error)
      read.onsuccess = () => {
        const cur = read.result as LibraryEntry | undefined
        const next: LibraryEntry = {
          ...cur,
          localId,
          doc,
          updatedAt: Date.now(),
          thumb: thumb ?? cur?.thumb,
          dirty: cur?.cloudId ? docHash(doc) !== cur.syncedHash : false,
        }
        const write = os.put(next, localId)
        write.onerror = () => reject(write.error)
        write.onsuccess = () => resolve(next)
      }
    })
  })
}

export async function createEntry(doc: ForgeDoc, thumb?: string): Promise<string> {
  const localId = uid("d")
  await saveEntry({ localId, doc, updatedAt: Date.now(), thumb })
  return localId
}

export async function deleteEntry(localId: string): Promise<void> {
  await del(localId, store)
}
