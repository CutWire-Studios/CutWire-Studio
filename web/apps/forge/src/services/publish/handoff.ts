import { createStore, set } from "idb-keyval"
import type { Kind } from "@cutwire/forge-core/doc/types"

/** The community upload page reads this database/store/key; see web/apps/shell/docs/publish-handoff.md. */
export const HANDOFF_DB = "cutwire-handoff"
export const HANDOFF_STORE = "handoff"
export const HANDOFF_KEY = "studio:publish-handoff"

export type CommunityKind = "effect" | "transition" | "audio-effect"

export interface PublishHandoff {
  kind: CommunityKind
  packageBlob: Blob
  filename: string
  thumbnailBlob?: Blob
  previewStripBlob?: Blob
  title?: string
  description?: string
  source: "forge"
}

export const communityKind = (k: Kind): CommunityKind => (k === "audio" ? "audio-effect" : k)

export function buildHandoff(input: {
  kind: Kind
  packageBytes: Uint8Array
  filename: string
  thumbnail: Blob
  previewStrip?: Blob
  title: string
  description: string
}): PublishHandoff {
  const kind = communityKind(input.kind)
  return {
    kind,
    packageBlob: new Blob([input.packageBytes as BlobPart], { type: "application/octet-stream" }),
    filename: input.filename,
    thumbnailBlob: input.thumbnail,
    ...(kind === "transition" && input.previewStrip ? { previewStripBlob: input.previewStrip } : {}),
    title: input.title,
    description: input.description,
    source: "forge",
  }
}

export async function writeHandoff(h: PublishHandoff): Promise<void> {
  await set(HANDOFF_KEY, h, createStore(HANDOFF_DB, HANDOFF_STORE))
}

export const uploadUrl = (kind: CommunityKind) => `/community/upload?kind=${kind}`
