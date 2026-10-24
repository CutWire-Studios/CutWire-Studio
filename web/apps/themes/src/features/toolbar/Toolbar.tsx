import { BUILTIN_NAMES } from "@cutwire/drift-theme"
import { Button, DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger, IconButton, Input, Select, Switch, toast, Tooltip } from "@cutwire/ui"
import { ArrowLeft, Download, FileDiff, Redo2, Send, Undo2 } from "lucide-react"
import { useState } from "react"
import { useStore } from "zustand"
import { buildJsonFile, buildZipFile, download } from "@/services/publish"
import { useComputed } from "@/state/computed"
import { isDual, redo, undo, useEditor } from "@/state/editor"
import { useUi } from "@/state/ui"
import { PublishDialog } from "../publish/PublishDialog"
import { DiffDialog } from "./DiffDialog"

export function exportFile(kind: "json" | "zip") {
  const { doc } = useEditor.getState()
  try {
    const pkg = kind === "json" ? buildJsonFile(doc) : buildZipFile(doc)
    download(pkg.fileName, pkg.blob)
    toast.success(`Exported ${pkg.fileName}`)
  } catch (e) {
    toast.error(e instanceof Error ? e.message : "Export failed")
  }
}

const baseOptions = BUILTIN_NAMES.map((n) => ({ value: n, label: n }))

function useHistoryState() {
  const past = useEditorHistory((t) => t.pastStates.length)
  const future = useEditorHistory((t) => t.futureStates.length)
  return { canUndo: past > 0, canRedo: future > 0 }
}

function useEditorHistory<T>(sel: (t: ReturnType<typeof useEditor.temporal.getState>) => T): T {
  return useStore(useEditor.temporal, sel)
}

export function Toolbar() {
  const doc = useEditor((s) => s.doc)
  const side = useEditor((s) => s.side)
  const setMeta = useEditor((s) => s.setMeta)
  const setDual = useEditor((s) => s.setDual)
  const setSide = useEditor((s) => s.setSide)
  const setView = useUi((s) => s.setView)
  const { validation, resolved } = useComputed()
  const { canUndo, canRedo } = useHistoryState()
  const [publish, setPublish] = useState(false)
  const [diff, setDiff] = useState(false)
  const dual = isDual(doc)
  const name = typeof doc.name === "string" ? doc.name : ""
  const nameError = validation.errors.find((e) => e.path === "name")
  const extendsValue = typeof doc.extends === "string" ? doc.extends : "none"
  const variant = resolved.variant

  return (
    <div className="flex flex-wrap items-center gap-2 border-b border-border bg-surface px-3 py-2">
      <Tooltip content="Back to start">
        <IconButton label="Back to start" onClick={() => setView("start")}>
          <ArrowLeft className="size-4" aria-hidden />
        </IconButton>
      </Tooltip>
      <Input aria-label="Theme name" aria-invalid={!!nameError} title={nameError?.message} className="w-48" value={name} onChange={(e) => setMeta({ name: e.target.value })} />
      <label className="flex items-center gap-2 text-xs text-muted-foreground">
        Extends
        <Select
          className="w-32"
          value={extendsValue}
          onValueChange={(v) => setMeta({ extends: v === "none" ? "" : v })}
          options={[{ value: "none", label: "nothing" }, ...baseOptions]}
        />
      </label>
      <label className="flex items-center gap-2 text-xs text-muted-foreground">
        Variant
        <Select className="w-28" value={dual ? side : variant} disabled={dual} onValueChange={(v) => setMeta({ variant: v as "dark" | "light" })} options={[{ value: "dark", label: "dark" }, { value: "light", label: "light" }]} />
      </label>
      <label className="flex items-center gap-2 text-xs text-muted-foreground" title="Ship a dark and a light side; the preview toggle edits one at a time.">
        Dark + light
        <Switch checked={dual} onCheckedChange={setDual} aria-label="Dark and light sides" />
      </label>
      {dual && (
        <div className="inline-flex rounded-full bg-muted p-0.5 text-xs" role="group" aria-label="Preview side">
          {(["dark", "light"] as const).map((s) => (
            <button key={s} type="button" aria-pressed={side === s} onClick={() => setSide(s)} className={`cursor-pointer rounded-full px-3 py-1 font-medium ${side === s ? "bg-surface shadow-sm" : "text-muted-foreground"}`}>
              {s}
            </button>
          ))}
        </div>
      )}
      <div className="ml-auto flex items-center gap-1">
        <Tooltip content="Undo (Ctrl+Z)">
          <IconButton label="Undo" disabled={!canUndo} onClick={undo}>
            <Undo2 className="size-4" aria-hidden />
          </IconButton>
        </Tooltip>
        <Tooltip content="Redo (Ctrl+Shift+Z)">
          <IconButton label="Redo" disabled={!canRedo} onClick={redo}>
            <Redo2 className="size-4" aria-hidden />
          </IconButton>
        </Tooltip>
        <Tooltip content="Show what differs from the base">
          <IconButton label="Overrides" onClick={() => setDiff(true)}>
            <FileDiff className="size-4" aria-hidden />
          </IconButton>
        </Tooltip>
        <DropdownMenu>
          <DropdownMenuTrigger asChild>
            <Button variant="secondary" size="sm">
              <Download className="size-4" aria-hidden /> Export
            </Button>
          </DropdownMenuTrigger>
          <DropdownMenuContent align="end">
            <DropdownMenuItem onSelect={() => exportFile("json")}>Download .json (Ctrl+S)</DropdownMenuItem>
            <DropdownMenuItem onSelect={() => exportFile("zip")}>Download .zip</DropdownMenuItem>
          </DropdownMenuContent>
        </DropdownMenu>
        <Button size="sm" onClick={() => setPublish(true)}>
          <Send className="size-4" aria-hidden /> Publish to Community
        </Button>
      </div>
      <PublishDialog open={publish} onOpenChange={setPublish} />
      <DiffDialog open={diff} onOpenChange={setDiff} />
    </div>
  )
}
