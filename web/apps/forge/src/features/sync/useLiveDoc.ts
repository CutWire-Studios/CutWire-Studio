import { useEffect } from "react"
import { sync, useSync } from "@/services/cloud"

/** While the editor has a cloud document open, follows its event stream (MCP, AI and other tabs). */
export function useLiveDoc(localId: string | null) {
  const cloudId = useSync((s) => (localId ? s.meta[localId]?.cloudId : undefined))
  const signedIn = useSync((s) => s.signedIn)
  useEffect(() => {
    if (!localId || !cloudId || !signedIn) return
    return sync.subscribe(localId, cloudId)
  }, [localId, cloudId, signedIn])
}
