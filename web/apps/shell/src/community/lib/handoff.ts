export const HANDOFF_DB = "cutwire-handoff"
export const HANDOFF_STORE = "handoff"
export const HANDOFF_KEY = "studio:publish-handoff"
/** Written by Theme Maker before the unified key existed. Still read; see docs/publish-handoff.md. */
export const LEGACY_THEME_KEY = "theme-upload"

export interface Handoff {
  kind: string
  filename: string
  packageBlob: Blob
  /** Media blobs by MediaSpec role (thumbnail, preview-strip, screenshot). */
  media: Record<string, Blob>
  title?: string
  description?: string
  source?: string
  key: string
}

const isBlob = (v: unknown): v is Blob => typeof Blob !== "undefined" && v instanceof Blob
const str = (v: unknown) => (typeof v === "string" && v.trim() ? v : undefined)
const obj = (v: unknown): Record<string, unknown> | null => (v && typeof v === "object" ? (v as Record<string, unknown>) : null)

/** Normalises a stored record (unified shape or Theme Maker's v1 `ThemeHandoff`) or returns null if unusable. */
export function parseHandoff(raw: unknown, key: string = HANDOFF_KEY): Handoff | null {
  const r = obj(raw)
  if (!r) return null

  // Unified: { kind, packageBlob, filename, thumbnailBlob?, previewStripBlob?, screenshotBlob?, title?, description? }
  if (typeof r.kind === "string" && isBlob(r.packageBlob)) {
    const media: Record<string, Blob> = {}
    if (isBlob(r.thumbnailBlob)) media.thumbnail = r.thumbnailBlob
    if (isBlob(r.previewStripBlob)) media["preview-strip"] = r.previewStripBlob
    if (isBlob(r.screenshotBlob)) media.screenshot = r.screenshotBlob
    return {
      kind: r.kind,
      filename: str(r.filename) ?? "package",
      packageBlob: r.packageBlob,
      media,
      title: str(r.title),
      description: str(r.description),
      source: str(r.source),
      key,
    }
  }

  // Theme Maker v1
  const pkg = obj(r.package)
  if (r.kind === "theme" && pkg && isBlob(pkg.blob)) {
    const shot = obj(r.screenshot)
    const meta = obj(r.meta)
    return {
      kind: "theme",
      filename: str(pkg.fileName) ?? "theme.json",
      packageBlob: pkg.blob,
      media: shot && isBlob(shot.blob) ? { screenshot: shot.blob } : {},
      title: str(meta?.name),
      source: str(r.source) ?? "themes",
      key,
    }
  }
  return null
}

function openDb(): Promise<IDBDatabase> {
  return new Promise((resolve, reject) => {
    const req = indexedDB.open(HANDOFF_DB)
    req.onupgradeneeded = () => {
      if (!req.result.objectStoreNames.contains(HANDOFF_STORE)) req.result.createObjectStore(HANDOFF_STORE)
    }
    req.onsuccess = () => resolve(req.result)
    req.onerror = () => reject(req.error)
  })
}

function request<T>(mode: IDBTransactionMode, fn: (s: IDBObjectStore) => IDBRequest<T>): Promise<T> {
  return openDb().then(
    (db) =>
      new Promise<T>((resolve, reject) => {
        const tx = db.transaction(HANDOFF_STORE, mode)
        const req = fn(tx.objectStore(HANDOFF_STORE))
        tx.oncomplete = () => {
          db.close()
          resolve(req.result)
        }
        tx.onerror = tx.onabort = () => {
          db.close()
          reject(tx.error)
        }
      }),
  )
}

/** Reads the first usable handoff for `kind` (or any kind when omitted). Unified key wins over the legacy theme key. */
export async function readHandoff(kind?: string | null): Promise<Handoff | null> {
  if (typeof indexedDB === "undefined") return null
  try {
    for (const key of [HANDOFF_KEY, LEGACY_THEME_KEY]) {
      const h = parseHandoff(await request("readonly", (s) => s.get(key)), key)
      if (h && (!kind || h.kind === kind)) return h
    }
  } catch {
    // IndexedDB can be unavailable (private mode); behave as if there is no handoff.
  }
  return null
}

export async function clearHandoff(key: string): Promise<void> {
  try {
    await request("readwrite", (s) => s.delete(key))
  } catch {
    // Nothing to clear.
  }
}
