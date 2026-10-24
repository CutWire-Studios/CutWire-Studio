import { ApiError, community, REPORT_REASONS, type ReportReason } from "@cutwire/api-client"
import { Button, Dialog, DialogContent, Select, Textarea, toast } from "@cutwire/ui"
import { Flag } from "lucide-react"
import { useState } from "react"
import { useLocation, useNavigate } from "react-router-dom"
import { useMe } from "../account/auth"
import { withReturn } from "../account/lib"

export const REASON_LABELS: Record<ReportReason, string> = {
  spam: "Spam",
  low_quality: "Low quality",
  broken: "Broken or does not work",
  offensive: "Offensive",
  copyright: "Copyright",
  malware: "Malware or unsafe",
  other: "Other",
}

export function ReportDialog({ itemId }: { itemId: string }) {
  const { me } = useMe()
  const navigate = useNavigate()
  const loc = useLocation()
  const [open, setOpen] = useState(false)
  const [reason, setReason] = useState<ReportReason>("spam")
  const [note, setNote] = useState("")
  const [busy, setBusy] = useState(false)

  const submit = async () => {
    setBusy(true)
    try {
      await community.report(itemId, reason, note.trim())
      toast.success("Thanks, your report was sent to the moderators.")
      setOpen(false)
      setNote("")
    } catch (e) {
      toast.error(e instanceof ApiError ? e.detail ?? e.title : "Could not send the report.")
    } finally {
      setBusy(false)
    }
  }

  return (
    <>
      <Button
        variant="ghost"
        size="sm"
        onClick={() => (me ? setOpen(true) : navigate(withReturn("/account/sign-in", loc.pathname + loc.search)))}
      >
        <Flag className="size-4" aria-hidden /> Report
      </Button>
      <Dialog open={open} onOpenChange={setOpen}>
        <DialogContent title="Report this item" description="Tell the moderators what is wrong. Reports are reviewed by people.">
          <div className="space-y-4">
            <Select value={reason} onValueChange={(v) => setReason(v as ReportReason)} options={REPORT_REASONS.map((r) => ({ value: r, label: REASON_LABELS[r] }))} />
            <Textarea value={note} onChange={(e) => setNote(e.target.value)} maxLength={500} rows={4} placeholder="Details (optional)" aria-label="Details" />
            <div className="flex justify-end gap-2">
              <Button variant="secondary" onClick={() => setOpen(false)}>
                Cancel
              </Button>
              <Button disabled={busy} onClick={() => void submit()}>
                Send report
              </Button>
            </div>
          </div>
        </DialogContent>
      </Dialog>
    </>
  )
}
