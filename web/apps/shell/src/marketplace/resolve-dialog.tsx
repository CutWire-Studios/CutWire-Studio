import { Button, Dialog, DialogContent, DialogTrigger, Input, Spinner } from "@cutwire/ui"
import { Link2 } from "lucide-react"
import { useState, type FormEvent } from "react"
import { Link } from "react-router-dom"
import { ItemCard, itemHref } from "./cards"
import { useResolveJob } from "./hooks"

export function ResolveDialog() {
  const [open, setOpen] = useState(false)
  const [url, setUrl] = useState("")
  const { job, error, busy, resolve, reset } = useResolveJob()

  const submit = (e: FormEvent) => {
    e.preventDefault()
    if (url.trim()) resolve(url.trim())
  }
  const failure = job?.status === "failed" ? (job.error?.detail ?? "Could not resolve this link.") : error?.message

  return (
    <Dialog
      open={open}
      onOpenChange={(o) => {
        setOpen(o)
        if (!o) reset()
      }}
    >
      <DialogTrigger asChild>
        <Button variant="secondary">
          <Link2 className="size-4" aria-hidden /> Paste a link
        </Button>
      </DialogTrigger>
      <DialogContent title="Paste a link" description="Paste a link to a clip or track from a supported site and we will look it up.">
        <form onSubmit={submit} className="flex gap-2">
          <Input value={url} onChange={(e) => setUrl(e.target.value)} placeholder="https://..." aria-label="Link" type="url" required />
          <Button type="submit" disabled={busy}>
            {busy ? <Spinner /> : "Look up"}
          </Button>
        </form>
        <div aria-live="polite" className="mt-4 space-y-3">
          {busy && <p className="text-sm text-muted-foreground">Looking it up...</p>}
          {failure && <p className="text-sm text-destructive">{failure}</p>}
          {job?.status === "ready" && job.item && (
            <div className="space-y-3">
              <div className="max-w-xs">
                <ItemCard item={job.item} />
              </div>
              <Button asChild onClick={() => setOpen(false)}>
                <Link to={itemHref(job.item)}>Open item</Link>
              </Button>
            </div>
          )}
        </div>
      </DialogContent>
    </Dialog>
  )
}
