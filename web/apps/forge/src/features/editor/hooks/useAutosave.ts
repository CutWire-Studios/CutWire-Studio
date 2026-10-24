import { useEffect, useState } from "react"
import type { ForgeDoc } from "@cutwire/forge-core/doc/types"
import { sync } from "@/services/cloud"
import { getEngine } from "@/services/preview/engine"
import { saveDoc } from "@/services/storage/library"

const AUTOSAVE_DELAY_MS = 700

/** Saves the document to this browser shortly after edits stop; true once the latest edit is saved. */
export function useAutosave(doc: ForgeDoc | null, localId: string | null, routeId: string, withThumb: boolean): boolean {
  const [savedDoc, setSavedDoc] = useState<ForgeDoc | null>(null)
  const saving = !!doc && !!localId && localId === routeId
  useEffect(() => {
    if (!doc || !localId || localId !== routeId) return
    const t = setTimeout(async () => {
      const entry = await saveDoc(localId, doc, withThumb ? getEngine().snapshot() : undefined)
      sync.noteSaved(entry)
      setSavedDoc(doc)
    }, AUTOSAVE_DELAY_MS)
    return () => clearTimeout(t)
  }, [doc, localId, routeId, withThumb])
  return !saving || savedDoc === doc
}
