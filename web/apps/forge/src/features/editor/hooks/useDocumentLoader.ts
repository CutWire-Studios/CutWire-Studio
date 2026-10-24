import { useEffect, useState } from "react"
import { useNavigate } from "react-router"
import type { ForgeDoc } from "@cutwire/forge-core/doc/types"
import { sync } from "@/services/cloud"
import { loadEntry } from "@/services/storage/library"
import { useEditor } from "@/state/editor"

/** Opens the library entry `id` in the editor, fetching it from the cloud if needed; true if it can't be found. */
export function useDocumentLoader(id: string): boolean {
  const navigate = useNavigate()
  const [missingId, setMissingId] = useState<string | null>(null)
  useEffect(() => {
    let live = true
    const open = (localId: string, doc: ForgeDoc) => {
      if (!live) return
      useEditor.getState().load(localId, doc)
      if (localId !== id) navigate(`/edit/${localId}`, { replace: true })
    }
    loadEntry(id)
      .then(async (e) => {
        if (e) {
          void sync.loadMeta(id)
          return open(e.localId, e.doc)
        }
        const localId = await sync.hydrateByRoute(id)
        const hydrated = localId ? await loadEntry(localId) : undefined
        if (!live) return
        if (hydrated) open(hydrated.localId, hydrated.doc)
        else setMissingId(id)
      })
      .catch(() => live && setMissingId(id))
    return () => {
      live = false
    }
  }, [id, navigate])
  return missingId === id
}
