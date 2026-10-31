import { useEffect } from "react"
import { Navigate, useNavigate, useSearchParams } from "react-router"
import { errorMessage } from "@cutwire/forge-core/errors"
import type { Kind } from "@cutwire/forge-core/doc/types"
import { emptyDoc } from "@cutwire/forge-core/doc/util"
import { STARTERS } from "@cutwire/forge-core/starters"
import { createSyncedEntry } from "@/services/cloud"
import { toast } from "@/shared/ui/toast"
import { freshCopy } from "./Home"

const KINDS: readonly string[] = ["effect", "transition", "audio"]

export function NewDocument() {
  const navigate = useNavigate()
  const kind = useSearchParams()[0].get("kind")
  const valid = kind !== null && KINDS.includes(kind)

  useEffect(() => {
    if (!valid) return
    const ofKind = STARTERS.filter((s) => s.doc.kind === (kind as Kind))
    const starter = ofKind.find((s) => s.name.startsWith("Blank")) ?? ofKind[0]
    createSyncedEntry(starter ? freshCopy(starter.doc) : emptyDoc(kind as Kind))
      .then((id) => navigate(`/edit/${id}`, { replace: true }))
      .catch((e) => {
        toast(errorMessage(e), "error")
        navigate("/", { replace: true })
      })
  }, [valid, kind, navigate])

  if (!valid) return <Navigate to="/" replace />
  return <main className="page empty-page" />
}
