import { useEffect } from "react"
import { saveDraft } from "@/services/drafts"
import { useEditor } from "@/state/editor"

const DELAY_MS = 800

/** Saves the open theme to IndexedDB (and to the account, once that endpoint exists) shortly after each change. */
export function useAutosave(enabled: boolean) {
  useEffect(() => {
    if (!enabled) return
    let timer: ReturnType<typeof setTimeout> | undefined
    const flush = () => {
      const { draftId, doc } = useEditor.getState()
      if (!draftId) return
      void saveDraft({ id: draftId, name: typeof doc.name === "string" ? doc.name : "", doc, updatedAt: Date.now() }).catch(() => {})
    }
    const unsub = useEditor.subscribe((s, prev) => {
      if (s.doc === prev.doc) return
      clearTimeout(timer)
      timer = setTimeout(flush, DELAY_MS)
    })
    return () => {
      unsub()
      if (timer) {
        clearTimeout(timer)
        flush()
      }
    }
  }, [enabled])
}
