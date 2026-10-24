import { builtins, diffAgainstBase, exportJson } from "@cutwire/drift-theme"
import { Button, Dialog, DialogContent, toast } from "@cutwire/ui"
import { useMemo } from "react"
import { useEditor } from "@/state/editor"

export function DiffDialog({ open, onOpenChange }: { open: boolean; onOpenChange(o: boolean): void }) {
  const doc = useEditor((s) => s.doc)
  const replaceDoc = useEditor((s) => s.replaceDoc)
  const { text, before, after, changed } = useMemo(() => {
    if (!open) return { text: "", before: 0, after: 0, changed: false }
    const diff = diffAgainstBase(doc, builtins)
    const t = exportJson(diff)
    const b = new TextEncoder().encode(exportJson(doc)).length
    return { text: t, before: b, after: new TextEncoder().encode(t).length, changed: JSON.stringify(diff) !== JSON.stringify(doc) }
  }, [open, doc])
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent title="Overrides" description="Only what differs from the base theme. This is what gets exported and published." className="max-w-3xl">
        <p className="mb-2 text-xs text-muted-foreground">
          {(before / 1024).toFixed(1)} KB as edited, {(after / 1024).toFixed(1)} KB as overrides.
        </p>
        <pre className="max-h-[50vh] overflow-auto rounded-lg border border-border bg-surface p-3 font-mono text-xs">{text}</pre>
        <div className="mt-4 flex justify-end gap-2">
          <Button variant="secondary" onClick={() => void navigator.clipboard.writeText(text).then(() => toast.success("Copied"))}>
            Copy
          </Button>
          <Button
            disabled={!changed}
            onClick={() => {
              replaceDoc(diffAgainstBase(doc, builtins))
              onOpenChange(false)
            }}
          >
            Remove redundant overrides
          </Button>
        </div>
      </DialogContent>
    </Dialog>
  )
}
