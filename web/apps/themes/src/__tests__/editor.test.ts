import { getPath } from "@cutwire/drift-theme"
import { beforeEach, describe, expect, it } from "vitest"
import { compute } from "../state/computed"
import { isDual, newDoc, redo, undo, useEditor } from "../state/editor"

const reset = (doc = newDoc("t", "dark", "dark")) => useEditor.getState().load("id", doc)

describe("editor store", () => {
  beforeEach(() => reset())

  it("writes tokens into the body and resets them", () => {
    useEditor.getState().setToken("palette.primary", "#ff0066")
    expect(getPath(useEditor.getState().doc, "palette.primary")).toBe("#ff0066")
    useEditor.getState().resetToken("palette.primary")
    expect(useEditor.getState().doc.palette).toBeUndefined()
  })

  it("undoes and redoes", () => {
    useEditor.getState().setToken("palette.primary", "#ff0066")
    undo()
    expect(useEditor.getState().doc.palette).toBeUndefined()
    redo()
    expect(getPath(useEditor.getState().doc, "palette.primary")).toBe("#ff0066")
  })

  it("edits the previewed side's overlay when dark and light are both shipped", () => {
    const s = useEditor.getState()
    s.setDual(true)
    expect(isDual(useEditor.getState().doc)).toBe(true)
    s.setSide("light")
    s.setToken("palette.primary", "#00ff00")
    const doc = useEditor.getState().doc
    expect(getPath(doc, "variants.light.palette.primary")).toBe("#00ff00")
    expect(doc.palette).toBeUndefined()
    expect(getPath(compute(doc, "light").resolved.document, "semantic.primary")).toBe("#00ff00")
    expect(getPath(compute(doc, "dark").resolved.document, "semantic.primary")).toBe("#F8B81C")
  })

  it("turning the sides off folds the shown side into the body", () => {
    const s = useEditor.getState()
    s.setDual(true)
    s.setToken("palette.primary", "#112233")
    s.setDual(false)
    const doc = useEditor.getState().doc
    expect(doc.variants).toBeUndefined()
    expect(getPath(doc, "palette.primary")).toBe("#112233")
  })

  it("sets name, variant and extends", () => {
    useEditor.getState().setMeta({ name: "x", variant: "light", extends: "frost" })
    expect(useEditor.getState().doc).toMatchObject({ name: "x", variant: "light", extends: "frost" })
    useEditor.getState().setMeta({ extends: "" })
    expect(useEditor.getState().doc.extends).toBeUndefined()
  })
})

describe("compute", () => {
  it("survives an unknown base and reports it", () => {
    const c = compute({ name: "x", extends: "nope" }, "dark")
    expect(c.error).toMatch(/unknown theme/)
    expect(c.validation.valid).toBe(false)
    expect(getPath(c.resolved.document, "semantic.surface.app")).toBe("#0d0d0d")
  })
})
