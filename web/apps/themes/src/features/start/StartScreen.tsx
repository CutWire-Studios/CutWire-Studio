import { BUILTIN_NAMES, builtins, getPath, importTheme, resolveTheme, ThemeImportError, toCss, validateTheme, type ThemeDoc, type Variant } from "@cutwire/drift-theme"
import { Button, Card, Input, toast } from "@cutwire/ui"
import { FileUp, FolderOpen, Plus, Trash2 } from "lucide-react"
import { useEffect, useMemo, useRef, useState } from "react"
import { deleteDraft, listDrafts, type Draft } from "@/services/drafts"
import { newDoc, useEditor } from "@/state/editor"
import { useUi } from "@/state/ui"

const swatches = Object.fromEntries(
  BUILTIN_NAMES.map((n) => {
    const r = resolveTheme(builtins[n], builtins)
    const g = (p: string) => toCss(String(getPath(r.document, p) ?? "#000"))
    return [n, { variant: r.variant, app: g("semantic.surface.app"), panel: g("semantic.surface.panel"), primary: g("semantic.primary"), fg: g("semantic.foreground") }]
  }),
) as Record<string, { variant: Variant; app: string; panel: string; primary: string; fg: string }>

function open(id: string, doc: ThemeDoc) {
  useEditor.getState().load(id, doc)
  useUi.getState().select(null)
  useUi.getState().setView("editor")
}

function NewTheme() {
  const [name, setName] = useState("my-theme")
  const [base, setBase] = useState<string>("dark")
  return (
    <Card className="flex flex-col gap-4">
      <h2 className="text-lg font-semibold">New theme</h2>
      <div className="grid grid-cols-2 gap-2 sm:grid-cols-4" role="radiogroup" aria-label="Start from">
        {BUILTIN_NAMES.map((n) => {
          const s = swatches[n]
          return (
            <button key={n} type="button" role="radio" aria-checked={base === n} onClick={() => setBase(n)} className={`flex cursor-pointer flex-col gap-2 rounded-xl border p-2 text-left ${base === n ? "border-primary ring-2 ring-primary/40" : "border-border"}`}>
              <span className="flex h-14 overflow-hidden rounded-lg border border-border" style={{ background: s.app }}>
                <span className="m-1.5 flex-1 rounded-md" style={{ background: s.panel }} />
                <span className="my-1.5 mr-1.5 w-4 rounded-md" style={{ background: s.primary }} />
              </span>
              <span className="text-sm font-medium">{n}</span>
              <span className="-mt-1 text-[11px] text-muted-foreground">{s.variant}{n === "graphite" || n === "frost" ? " · translucent" : ""}</span>
            </button>
          )
        })}
      </div>
      <label className="flex flex-col gap-1 text-sm">
        Name
        <Input value={name} onChange={(e) => setName(e.target.value)} />
      </label>
      <Button className="self-start" disabled={!name.trim()} onClick={() => open(crypto.randomUUID(), newDoc(name.trim(), base, swatches[base].variant))}>
        <Plus className="size-4" aria-hidden /> Create
      </Button>
    </Card>
  )
}

function ImportTheme() {
  const input = useRef<HTMLInputElement>(null)
  const [problems, setProblems] = useState<string[]>([])
  const [over, setOver] = useState(false)

  const take = async (file: File | undefined) => {
    if (!file) return
    setProblems([])
    try {
      const doc = importTheme(file.name, new Uint8Array(await file.arrayBuffer()))
      const check = validateTheme(doc, { requireName: false })
      if (typeof doc.name !== "string" || !doc.name) doc.name = file.name.replace(/\.(json|zip)$/i, "")
      if (check.errors.length) toast.warning(`Opened with ${check.errors.length} problem${check.errors.length > 1 ? "s" : ""}. See the Issues tab.`)
      open(crypto.randomUUID(), doc)
    } catch (e) {
      setProblems([e instanceof ThemeImportError || e instanceof Error ? e.message : String(e)])
    }
  }

  return (
    <Card className="flex flex-col gap-3">
      <h2 className="text-lg font-semibold">Import</h2>
      <div
        onDragOver={(e) => {
          e.preventDefault()
          setOver(true)
        }}
        onDragLeave={() => setOver(false)}
        onDrop={(e) => {
          e.preventDefault()
          setOver(false)
          void take(e.dataTransfer.files[0])
        }}
        className={`flex flex-col items-center gap-2 rounded-xl border-2 border-dashed p-6 text-center text-sm ${over ? "border-primary bg-muted" : "border-border"}`}
      >
        <FileUp className="size-6 text-muted-foreground" aria-hidden />
        <p>Drop a Drift theme here, a .json or a .zip.</p>
        <p className="text-xs text-muted-foreground">Themes with QML overrides are refused.</p>
        <Button variant="secondary" size="sm" onClick={() => input.current?.click()}>
          Choose file
        </Button>
        <input ref={input} type="file" accept=".json,.zip,application/json,application/zip" className="hidden" onChange={(e) => void take(e.target.files?.[0])} />
      </div>
      {problems.map((p) => (
        <p key={p} role="alert" className="text-sm text-destructive">
          {p}
        </p>
      ))}
    </Card>
  )
}

function Drafts() {
  const [drafts, setDrafts] = useState<Draft[] | null>(null)
  useEffect(() => {
    let dead = false
    listDrafts()
      .then((d) => !dead && setDrafts(d))
      .catch(() => !dead && setDrafts([]))
    return () => {
      dead = true
    }
  }, [])
  const fmt = useMemo(() => new Intl.DateTimeFormat(undefined, { dateStyle: "medium", timeStyle: "short" }), [])
  return (
    <Card className="flex flex-col gap-3">
      <h2 className="text-lg font-semibold">Drafts</h2>
      {drafts === null && <p className="text-sm text-muted-foreground">Loading</p>}
      {drafts?.length === 0 && <p className="text-sm text-muted-foreground">No saved drafts yet. Your work is saved here automatically.</p>}
      <ul className="flex flex-col gap-1">
        {drafts?.map((d) => (
          <li key={d.id} className="flex items-center gap-2 rounded-lg px-2 py-1.5 hover:bg-muted">
            <FolderOpen className="size-4 shrink-0 text-muted-foreground" aria-hidden />
            <button type="button" className="min-w-0 flex-1 cursor-pointer text-left" onClick={() => open(d.id, d.doc)}>
              <span className="block truncate text-sm font-medium">{d.name || "untitled"}</span>
              <span className="text-xs text-muted-foreground">{fmt.format(d.updatedAt)}</span>
            </button>
            <Button variant="ghost" size="sm" aria-label={`Delete ${d.name}`} onClick={() => void deleteDraft(d.id).then(() => setDrafts((l) => l?.filter((x) => x.id !== d.id) ?? null))}>
              <Trash2 className="size-4" aria-hidden />
            </Button>
          </li>
        ))}
      </ul>
    </Card>
  )
}

export function StartScreen() {
  return (
    <div className="h-dvh overflow-y-auto pt-24 pb-12">
      <div className="mx-auto flex max-w-5xl flex-col gap-6 px-4">
        <div>
          <h1 className="text-headline-lg">Drift Theme Maker</h1>
          <p className="mt-2 max-w-2xl text-muted-foreground">Design a theme for the Drift video editor, see it on a mock of the editor as you go, then export it or share it with the community.</p>
        </div>
        <NewTheme />
        <div className="grid gap-6 md:grid-cols-2">
          <ImportTheme />
          <Drafts />
        </div>
      </div>
    </div>
  )
}
