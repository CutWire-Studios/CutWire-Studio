import { clone, deletePath, deepEqual, getPath, mergeTheme, setPath, type Json, type JsonObject, type ThemeDoc, type Variant } from "@cutwire/drift-theme"
import { temporal } from "zundo"
import { create } from "zustand"

export function isDual(doc: ThemeDoc): boolean {
  const v = doc.variants
  return typeof v === "object" && v !== null && !Array.isArray(v) && "dark" in v && "light" in v
}

/** Where edits land: the theme body, or the overlay of the side being previewed when both exist. */
export function editPrefix(doc: ThemeDoc, side: Variant): string {
  return isDual(doc) ? `variants.${side}.` : ""
}

export function newDoc(name: string, base: string, variant: Variant): ThemeDoc {
  return { name, schemaVersion: 1, extends: base, variant }
}

export interface EditorState {
  draftId: string
  doc: ThemeDoc
  /** Which side the preview shows; only meaningful for dual-variant themes. Not part of undo. */
  side: Variant
  load(draftId: string, doc: ThemeDoc): void
  setToken(path: string, value: Json): void
  resetToken(path: string): void
  setMeta(patch: { name?: string; variant?: Variant; extends?: string }): void
  setDual(on: boolean): void
  setSide(side: Variant): void
  replaceDoc(doc: ThemeDoc): void
}

let burstTimer: ReturnType<typeof setTimeout> | undefined
let burstOpen = false

function closeBurst() {
  clearTimeout(burstTimer)
  burstOpen = false
}

/** Records one undo step per burst of changes (a colour drag, a slider scrub), not one per event. */
function burst<T>(handleSet: (state: T) => void) {
  return (state: T) => {
    if (!burstOpen) handleSet(state)
    burstOpen = true
    clearTimeout(burstTimer)
    burstTimer = setTimeout(() => (burstOpen = false), 400)
  }
}

export const useEditor = create<EditorState>()(
  temporal(
    (set, get) => ({
      draftId: "",
      doc: newDoc("untitled", "dark", "dark"),
      side: "dark",

      load: (draftId, doc) => {
        set({ draftId, doc, side: doc.variant === "light" ? "light" : "dark" })
        useEditor.temporal.getState().clear()
        closeBurst()
      },
      setToken: (path, value) => {
        const { doc, side } = get()
        set({ doc: setPath(doc, editPrefix(doc, side) + path, value) })
      },
      resetToken: (path) => {
        const { doc, side } = get()
        const next = deletePath(doc, editPrefix(doc, side) + path)
        if (next !== doc) set({ doc: next })
      },
      setMeta: (patch) => {
        const next: ThemeDoc = { ...get().doc }
        if (patch.name !== undefined) next.name = patch.name
        if (patch.variant) next.variant = patch.variant
        if (patch.extends !== undefined) {
          if (patch.extends) next.extends = patch.extends
          else delete next.extends
        }
        if (!deepEqual(next, get().doc)) set({ doc: next })
      },
      setDual: (on) => {
        const { doc, side } = get()
        if (on === isDual(doc)) return
        if (on) {
          const v = (typeof doc.variants === "object" && doc.variants !== null && !Array.isArray(doc.variants) ? doc.variants : {}) as JsonObject
          set({ doc: { ...doc, variants: { dark: v.dark ?? {}, light: v.light ?? {}, ...v } } })
          return
        }
        const { variants, ...rest } = doc
        const overlay = getPath(variants, side)
        const body = overlay ? (mergeTheme(rest, overlay) as ThemeDoc) : rest
        set({ doc: { ...body, variant: side } })
      },
      setSide: (side) => set({ side }),
      replaceDoc: (doc) => set({ doc: clone(doc) }),
    }),
    {
      partialize: (s) => ({ doc: s.doc }),
      equality: (a, b) => a.doc === b.doc,
      handleSet: (handleSet) => burst(handleSet),
      limit: 200,
    },
  ),
)

export function undo() {
  useEditor.temporal.getState().undo()
}
export function redo() {
  useEditor.temporal.getState().redo()
}
