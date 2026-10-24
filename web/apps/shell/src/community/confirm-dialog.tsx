import { Button, Dialog, DialogContent, Spinner, Textarea } from "@cutwire/ui"
import { useState, type ReactNode } from "react"

/** Confirmation dialog with an optional (or required) reason. `onConfirm` may throw; the dialog stays open and shows why. */
export function ConfirmDialog({
  open,
  title,
  description,
  confirmLabel = "Confirm",
  destructive,
  reason,
  children,
  onConfirm,
  onClose,
}: {
  open: boolean
  title: string
  description?: string
  confirmLabel?: string
  destructive?: boolean
  reason?: "none" | "optional" | "required"
  children?: ReactNode
  onConfirm: (reason: string) => Promise<void> | void
  onClose: () => void
}) {
  const [text, setText] = useState("")
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const mode = reason ?? "none"
  const missing = mode === "required" && text.trim().length < 3

  const run = async () => {
    setBusy(true)
    setError(null)
    try {
      await onConfirm(text.trim())
      setText("")
      onClose()
    } catch (e) {
      setError(e instanceof Error ? e.message : "That did not work.")
    } finally {
      setBusy(false)
    }
  }

  return (
    <Dialog open={open} onOpenChange={(o) => !o && !busy && onClose()}>
      <DialogContent title={title} description={description}>
        <div className="space-y-4">
          {children}
          {mode !== "none" && (
            <Textarea
              autoFocus
              rows={3}
              value={text}
              onChange={(e) => setText(e.target.value)}
              placeholder={mode === "required" ? "Reason (required)" : "Reason (optional)"}
              aria-label="Reason"
            />
          )}
          {error && (
            <p role="alert" className="text-sm text-destructive">
              {error}
            </p>
          )}
          <div className="flex justify-end gap-2">
            <Button variant="secondary" onClick={onClose} disabled={busy}>
              Cancel
            </Button>
            <Button variant={destructive ? "destructive" : "primary"} disabled={busy || missing} onClick={() => void run()}>
              {busy && <Spinner className="size-4" />} {confirmLabel}
            </Button>
          </div>
        </div>
      </DialogContent>
    </Dialog>
  )
}
