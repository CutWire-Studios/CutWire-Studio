import { useEffect, useState } from "react"
import { useNavigate } from "react-router"
import { errorMessage } from "@cutwire/forge-core/errors"
import { decodeLinkPayload, LINK_PREFIX } from "@cutwire/forge-core/export/link"
import { createEntry } from "@/services/storage/library"
import { toast } from "@/shared/ui/toast"

const sharePayload = () => location.hash.slice(1)

/**
 * Acts on what the URL carries: a sign-in error to show, or a share link to open. Share links
 * carry the whole document in the fragment, which never reaches a server. True while opening one.
 */
export function useUrlActions(): boolean {
  const navigate = useNavigate()
  const [opening, setOpening] = useState(() => sharePayload().startsWith(LINK_PREFIX))

  useEffect(() => {
    const err = new URLSearchParams(location.search).get("signin_error")
    if (err) {
      toast(err, "error")
      history.replaceState(null, "", import.meta.env.BASE_URL)
    }
  }, [])

  useEffect(() => {
    const payload = sharePayload()
    if (!payload.startsWith(LINK_PREFIX)) return
    decodeLinkPayload(payload)
      .then((doc) => createEntry(doc))
      .then((id) => navigate(`/edit/${id}`, { replace: true }))
      .catch((e) => {
        toast(errorMessage(e), "error")
        history.replaceState(null, "", import.meta.env.BASE_URL)
        setOpening(false)
      })
  }, [navigate])

  return opening
}
