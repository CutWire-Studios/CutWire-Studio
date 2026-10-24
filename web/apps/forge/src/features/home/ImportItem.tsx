import { useEffect, useState } from "react"
import { Link, useNavigate, useSearchParams } from "react-router"
import { community } from "@cutwire/api-client"
import { errorMessage } from "@cutwire/forge-core/errors"
import { importFile } from "@cutwire/forge-core/export/archive"
import { createSyncedEntry } from "@/services/cloud"
import { AppHeader } from "@/shared/ui/AppHeader"

const EDITABLE = new Set(["effect", "transition", "audio-effect"])

/** `/forge/import?item=<id>`: opens a community package in the editor. */
export function ImportItem() {
  const item = useSearchParams()[0].get("item")
  const navigate = useNavigate()
  const [error, setError] = useState<string | null>(item ? null : "No item was given to import.")

  useEffect(() => {
    if (!item) return
    let live = true
    ;(async () => {
      const detail = await community.getById(item)
      if (!EDITABLE.has(detail.kind)) throw new Error(`"${detail.title}" is a ${detail.kind}, which Drift Forge can't edit.`)
      const res = await fetch(community.downloadUrl(item), { credentials: "include" })
      if (!res.ok) throw new Error("Couldn't download this package.")
      const doc = await importFile("package.driftfx", new Uint8Array(await res.arrayBuffer()))
      const id = await createSyncedEntry(doc)
      if (live) navigate(`/edit/${id}`, { replace: true })
    })().catch((e) => live && setError(errorMessage(e)))
    return () => {
      live = false
    }
  }, [item, navigate])

  return (
    <>
      <AppHeader />
      <main className="page empty-page">
        {error ? (
          <>
            <h2>Couldn't open this item</h2>
            <p className="meta">{error}</p>
            <Link className="btn btn-primary" to="/">
              Back to your creations
            </Link>
          </>
        ) : (
          <h2>Opening in Drift Forge…</h2>
        )}
      </main>
    </>
  )
}
