import { Button, Dialog, DialogClose, DialogContent } from "@cutwire/ui"
import { ExternalLink } from "lucide-react"
import { useState, type ReactNode } from "react"
import { Link } from "react-router-dom"
import { itemPath, pagePath, resolveLink, type Action } from "./lib/layout"

/** Renders an action as a link. Same-origin links navigate; other https links ask before leaving. */
export function ActionButton({ action, children }: { action: Action; children: ReactNode }) {
  const [confirming, setConfirming] = useState(false)
  if (action.type === "open-page") {
    return (
      <Button asChild>
        <Link to={pagePath(action.page)}>{children}</Link>
      </Button>
    )
  }
  if (action.type === "open-item") {
    return (
      <Button asChild>
        <Link to={itemPath(action.source, action.id)}>{children}</Link>
      </Button>
    )
  }
  const target = resolveLink(action.url, window.location.origin)
  if (target.kind === "blocked") return null
  if (target.kind === "internal") {
    return (
      <Button asChild>
        <Link to={target.path}>{children}</Link>
      </Button>
    )
  }
  const host = new URL(target.url).host
  return (
    <>
      <Button onClick={() => setConfirming(true)}>
        {children} <ExternalLink className="size-4" aria-hidden />
      </Button>
      <Dialog open={confirming} onOpenChange={setConfirming}>
        <DialogContent title="Leave CutWire Studio?" description={`This link opens ${host}, a site we do not control.`}>
          <div className="flex justify-end gap-2">
            <DialogClose asChild>
              <Button variant="secondary">Stay</Button>
            </DialogClose>
            <Button asChild onClick={() => setConfirming(false)}>
              <a href={target.url} target="_blank" rel="noopener noreferrer">
                Open {host}
              </a>
            </Button>
          </div>
        </DialogContent>
      </Dialog>
    </>
  )
}
