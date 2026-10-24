import { useState } from "react"
import { signIn } from "@/services/account/client"
import { useAccount } from "@/services/account/account"
import { sync, useSync } from "@/services/cloud"
import { toast } from "@/shared/ui/toast"
import "./sync.css"

/** Home banner: a sign-in prompt for guests, an "Upload local library" action once signed in. */
export function SyncBanner({ localOnly, onDone }: { localOnly: number; onDone: () => void }) {
  const loaded = useAccount((s) => s.loaded)
  const signedIn = useSync((s) => s.signedIn)
  const [busy, setBusy] = useState(false)
  if (!loaded) return null
  if (!signedIn) {
    return (
      <div className="sync-banner" role="note">
        <span>Sign in to sync your library and use MCP.</span>
        <button type="button" className="btn btn-primary btn-sm" onClick={signIn}>
          Sign in
        </button>
      </div>
    )
  }
  if (!localOnly) return null
  return (
    <div className="sync-banner" role="note">
      <span>
        {localOnly} {localOnly === 1 ? "document is" : "documents are"} only in this browser.
      </span>
      <button
        type="button"
        className="btn btn-primary btn-sm"
        disabled={busy}
        onClick={async () => {
          setBusy(true)
          const r = await sync.uploadLocalLibrary()
          setBusy(false)
          const parts = [`${r.created} uploaded`]
          if (r.adopted) parts.push(`${r.adopted} already in the cloud`)
          if (r.conflict) parts.push(`${r.conflict} need a decision`)
          if (r.failed) parts.push(`${r.failed} failed`)
          toast(parts.join(", "), r.failed ? "error" : "info")
          onDone()
        }}
      >
        {busy ? "Uploading…" : "Upload local library"}
      </button>
    </div>
  )
}
