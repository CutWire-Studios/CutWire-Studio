import type { ForgeDoc } from "@cutwire/forge-core/doc/types"
import { base64ToBytes } from "@cutwire/forge-core/doc/util"
import { getEngine } from "@/services/preview/engine"
import { imageDataToPng } from "@/services/storage/assets"

function dataUrlBytes(url: string): Uint8Array {
  return base64ToBytes(url.slice(url.indexOf(",") + 1))
}

/** The picture a package carries: the effect thumbnail or the transition's preview strip. */
export async function previewPng(doc: ForgeDoc): Promise<Uint8Array | null> {
  if (doc.kind === "effect" && doc.preview.customThumb) return dataUrlBytes(doc.preview.customThumb)
  const img = getEngine().renderPackagePreview(doc)
  return typeof img === "string" ? null : imageDataToPng(img)
}
