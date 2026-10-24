import type { ForgeAsset, ForgeDoc } from "@cutwire/forge-core/doc/types"
import { base64ToBytes, bytesToBase64 } from "@cutwire/forge-core/doc/util"
import { forge } from "@cutwire/api-client"

/** A picture or audio file in a stored document: `data` locally, `blob` (sha256) in the cloud. */
export type SyncAsset = Omit<ForgeAsset, "data"> & { data?: string; blob?: string }

const shaCache = new Map<string, string>()

function cacheKey(a: SyncAsset): string {
  const d = a.data ?? ""
  return `${a.id}:${d.length}:${d.slice(0, 24)}:${d.slice(-24)}`
}

/**
 * Uploads every asset that has bytes but no blob reference yet and returns the document with
 * `blob` set on all of them. `data` is kept: the local copy stays usable offline.
 */
export async function uploadAssets(doc: ForgeDoc): Promise<ForgeDoc> {
  const assets: SyncAsset[] = []
  for (const a of doc.assets as SyncAsset[]) {
    if (a.blob) {
      assets.push(a)
      continue
    }
    if (!a.data) {
      assets.push(a)
      continue
    }
    const key = cacheKey(a)
    let sha = shaCache.get(key)
    if (!sha) {
      const bytes = base64ToBytes(a.data) as Uint8Array<ArrayBuffer>
      sha = (await forge.uploadAsset(bytes, a.mime)).sha256
      shaCache.set(key, sha)
    }
    assets.push({ ...a, blob: sha })
  }
  return { ...doc, assets: assets as unknown as ForgeAsset[] }
}

/** The document as the server stores it: assets are referenced by `blob`, never carry `data`. */
export function toCloudPayload(doc: ForgeDoc): unknown {
  const assets = (doc.assets as SyncAsset[]).map(({ data: _data, ...rest }) => rest)
  return { ...doc, assets }
}

/** A cloud document made editable here again: fetches each blob into `data`, reusing local bytes. */
export async function hydrateAssets(cloud: unknown, local?: ForgeDoc): Promise<ForgeDoc> {
  const doc = cloud as ForgeDoc
  const have = new Map((local?.assets as SyncAsset[] | undefined)?.filter((a) => a.data && a.blob).map((a) => [a.blob!, a.data!]))
  const assets: SyncAsset[] = []
  for (const a of (doc.assets ?? []) as SyncAsset[]) {
    if (a.data || !a.blob) {
      assets.push(a)
      continue
    }
    let data = have.get(a.blob)
    if (data === undefined) data = bytesToBase64(await forge.fetchBlob(a.blob))
    shaCache.set(cacheKey({ ...a, data }), a.blob)
    assets.push({ ...a, data })
  }
  return { ...doc, assets: assets as unknown as ForgeAsset[] }
}

/** Drops cloud-only bookkeeping so exports and packages carry the plain document. */
export function stripBlobRefs(doc: ForgeDoc): ForgeDoc {
  if (!(doc.assets as SyncAsset[]).some((a) => a.blob)) return doc
  return { ...doc, assets: (doc.assets as SyncAsset[]).map(({ blob: _blob, ...rest }) => rest) as unknown as ForgeAsset[] }
}
