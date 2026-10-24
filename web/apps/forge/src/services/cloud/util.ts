import { ApiError } from "@cutwire/api-client"
import type { ForgeDoc } from "@cutwire/forge-core/doc/types"
import { makeEffectId } from "@cutwire/forge-core/doc/util"
import type { SyncAsset } from "./assets"

export const isTransient = (e: unknown) => !(e instanceof ApiError) || e.status >= 500 || e.status === 429 || e.status === 408

export const thumbMark = (t: string) => `${t.length}:${t.slice(-32)}`

export function copyOf(doc: ForgeDoc, name: string): ForgeDoc {
  const d = structuredClone(doc)
  d.meta.displayName = name
  d.meta.id = makeEffectId(name)
  return d
}

export function sameAssets(local: ForgeDoc, pushed: ForgeDoc): boolean {
  const a = local.assets as SyncAsset[]
  const b = pushed.assets as SyncAsset[]
  return a.length === b.length && a.every((x, i) => x.id === b[i].id && (x.data?.length ?? 0) === (b[i].data?.length ?? 0))
}

