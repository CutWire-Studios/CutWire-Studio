import { beforeEach, describe, expect, it } from "vitest"
import { unzipSync } from "fflate"
import { compile } from "@cutwire/forge-core/compiler/compile"
import { minAppVersion, packageJson } from "@cutwire/forge-core/compiler/manifest"
import type { ForgeDoc } from "@cutwire/forge-core/doc/types"
import { exportZip } from "@cutwire/forge-core/export/archive"
import { parseForgeDoc } from "@cutwire/forge-core/export/link"
import { nodeDef } from "@cutwire/forge-core/nodes/registry"
import { useEditor } from "@/state/editor"
import { chain } from "./helpers"

const PIXEL = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=="

function load(doc: ForgeDoc) {
  useEditor.getState().load("t", doc)
  return useEditor.getState()
}
const doc = () => useEditor.getState().doc!
const node = (type: string) => doc().nodes.find((n) => n.type === type)!

describe("package extras", () => {
  beforeEach(() => load(chain("effect", ["twirl", "kaleidoscope"])))

  it("writes hints, show-when and presets, which today's Drift ignores", () => {
    const s = useEditor.getState()
    s.expose(node("kaleidoscope").id, "rotation")
    s.addParam({ identifier: "glowOn", displayName: "Glow", type: "bool", min: 0, max: 1, default: true })
    s.updateParam("rotation", { showWhen: { param: "glowOn", equals: true } })
    s.setParamValue("rotation", 45)
    s.savePreset("Tilted")
    const json = packageJson(doc(), compile(doc(), { mode: "export" })) as Record<string, unknown>
    const params = json.parameters as Record<string, unknown>[]
    expect(params[0]).toMatchObject({ type: "float", ui: { control: "angle", unit: "°" }, showWhen: { param: "glowOn", equals: true } })
    expect(json.presets).toEqual([{ name: "Tilted", values: { rotation: 45, glowOn: 1 } }])
    expect(json.nextFeatures).toBeUndefined()
  })
})

describe("controls", () => {
  beforeEach(() => {
    const d = chain("effect", ["twirl", "kaleidoscope", "gradient_ramp"])
    load(d)
  })

  it("exposes native point, int and dropdown parameters", () => {
    const s = useEditor.getState()
    s.expose(node("twirl").id, "center")
    s.expose(node("kaleidoscope").id, "segments")
    const choice = s.addNode("choice", 0, 0)
    expect(useEditor.getState().expose(choice, "value")).toBeNull()
    expect(doc().params.map((p) => p.type)).toEqual(["point", "int", "choice"])
    const r = compile(doc(), { mode: "export" })
    expect(r.ok).toBe(true)
    const src = r.passes.at(-1)!.source
    expect(src).toContain("uniform vec2 centre;")
    expect(src).toContain("vec2 pv_centre() { return centre; }")
    const json = packageJson(doc(), r) as { parameters: Record<string, unknown>[]; nextFeatures: string[] }
    expect(json.parameters[0]).toMatchObject({ type: "point", minValue: [0, 0], maxValue: [1, 1], defaultValue: [0.5, 0.5] })
    expect(json.parameters[2]).toMatchObject({ type: "choice", options: ["First", "Second", "Third"], defaultValue: 0 })
    expect(json.nextFeatures).toEqual(["param:choice", "param:int", "param:point"])
  })

  it("binds an exposed gradient as a named sampler", () => {
    expect(useEditor.getState().exposeOption(node("gradient_ramp").id, "stops")).toBeNull()
    const r = compile(doc(), { mode: "export" })
    const last = r.passes.at(-1)!
    expect(last.source).toContain("uniform sampler2D gradient;")
    expect(last.paramSamplers).toEqual(["gradient"])
    const json = packageJson(doc(), r) as { parameters: Record<string, unknown>[] }
    expect(json.parameters[0]).toMatchObject({ type: "gradient", defaultValue: [{ position: 0, color: "#0d0526ff" }, { position: 0.5 }, { position: 1 }] })
  })

  it("writes a picture parameter's default image into the package", () => {
    const s = useEditor.getState()
    s.addAsset({ id: "a1", name: "logo.png", mime: "image/png", data: PIXEL, width: 1, height: 1 })
    const img = s.addNode("image", 0, 0)
    useEditor.getState().setData(img, "asset", "a1")
    expect(useEditor.getState().exposeOption(img, "asset")).toBeNull()
    const files = Object.keys(unzipSync(exportZip(doc(), { png: null })))
    expect(files).toContain(`${doc().meta.id}/param_picture.png`)
  })

  it("creates a track parameter with the Other clip block and removes it with the block", () => {
    const s = useEditor.getState()
    const id = s.addNode("other_clip", 0, 0)
    expect(doc().params).toMatchObject([{ identifier: "otherClip", displayName: "Other track", type: "track" }])
    useEditor.getState().removeNodes([id])
    expect(doc().params).toEqual([])
  })

  it("loads a schema-2 clip parameter as a track parameter", () => {
    useEditor.getState().addNode("other_clip", 0, 0)
    const old = { ...doc(), forge: 2, params: doc().params.map((p) => ({ ...p, type: "clip" })) }
    const loaded = parseForgeDoc(JSON.stringify(old))
    expect(loaded.forge).toBe(3)
    expect(loaded.params).toMatchObject([{ identifier: "otherClip", type: "track" }])
  })

  it("needs Drift 0.9.0 for a track parameter and writes requires as a list", () => {
    useEditor.getState().addNode("other_clip", 0, 0)
    const compiled = compile(doc(), { mode: "export" })
    expect(minAppVersion(doc(), compiled)).toBe("0.9.0")
    const json = packageJson(doc(), { ...compiled, usesMask: true, usesDepth: true }) as Record<string, unknown>
    expect(json.requires).toEqual(["mask", "depth"])
    expect(json.nextFeatures).toEqual(["node:other_clip", "param:track", "requires:depth", "requires:mask"])
    expect((packageJson(doc(), { ...compiled, usesDepth: true }) as Record<string, unknown>).requires).toBe("depth")
  })

  it("renames a parameter everywhere it's referenced", () => {
    const s = useEditor.getState()
    s.exposeOption(node("gradient_ramp").id, "stops")
    expect(useEditor.getState().updateParam("gradient", { identifier: "heat" })).toBeNull()
    expect(node("gradient_ramp").data.stops).toEqual({ param: "heat" })
  })
})

describe("track, audio, depth and face blocks", () => {
  beforeEach(() => {
    const d = chain("effect", [])
    load({ ...d, edges: [] })
  })
  const outputId = () => doc().nodes.find((n) => nodeDef(n.type)?.output)!.id
  const show = (type: string, socket: string) => {
    const id = useEditor.getState().addNode(type, 0, 0)
    expect(useEditor.getState().connect(id, socket, outputId(), "image")).toBe(true)
    return id
  }
  const lastPass = () => compile(doc(), { mode: "export" }).passes.at(-1)!

  it.each([
    ["track_mask", "mask"],
    ["audio_track", "spectrum"],
    ["depth", "normal"],
    ["face", "center"],
  ])("%s compiles in both modes", (type, socket) => {
    show(type, socket)
    expect(compile(doc(), { mode: "export" }).ok).toBe(true)
    expect(compile(doc(), { mode: "preview" }).ok).toBe(true)
  })

  it("declares the audio slider's sampler and its three scalars", () => {
    show("audio_track", "level")
    const src = lastPass().source
    expect(src).toContain("uniform sampler2D audio;")
    for (const u of ["audio_level", "audio_bass", "audio_beat"]) expect(src).toContain(`uniform float ${u};`)
    expect(lastPass().paramSamplers).toEqual(["audio"])
  })

  it("passes the frame through where there is no face", () => {
    show("face", "valid")
    const src = lastPass().source
    expect(src).toContain("if (u_faceValid < 0.5) {\n        fragColor = texture(u_currentTexture, v_texCoord);")
    expect(src.match(/uniform float u_faceValid;/g)).toHaveLength(1)
    expect(compile(doc(), { mode: "export" }).usesFace).toBe(true)
  })

  it("adds a face index slider with the first face block and removes it with the last", () => {
    const id = show("face", "valid")
    expect(doc().params).toMatchObject([{ identifier: "faceIndex", type: "float", min: 0, max: 3, default: 0 }])
    useEditor.getState().removeNodes([id])
    expect(doc().params).toEqual([])
  })

  it("writes a requires list for mask and depth, and the next features of the new blocks", () => {
    const s = useEditor.getState()
    const mix = s.addNode("mix", 0, 0)
    const mask = s.addNode("clip_mask", 0, 0)
    const depth = s.addNode("depth", 0, 0)
    s.connect(mask, "mask", mix, "amount")
    s.connect(depth, "has", mix, "b")
    s.connect(mix, "image", outputId(), "image")
    const compiled = compile(doc(), { mode: "export" })
    expect(compiled.ok).toBe(true)
    expect(minAppVersion(doc(), compiled)).toBe("0.9.0")
    expect((packageJson(doc(), compiled) as Record<string, unknown>).requires).toEqual(["mask", "depth"])

    const f = useEditor.getState()
    f.addNode("track_mask", 0, 0)
    f.addNode("audio_track", 0, 0)
    f.addNode("face", 0, 0)
    const json = packageJson(doc(), compile(doc(), { mode: "export" })) as { nextFeatures: string[] }
    expect(json.nextFeatures).toEqual(["node:audio_track", "node:clip_mask", "node:depth", "node:face", "node:track_mask", "param:audio", "param:trackMask", "requires:depth", "requires:mask"])
  })

  it("keeps an audio slider's level, bass and beat names to itself", () => {
    const s = useEditor.getState()
    s.addNode("audio_track", 0, 0)
    s.addParam({ identifier: "lift", displayName: "Lift", type: "float", min: 0, max: 1, default: 0 })
    expect(useEditor.getState().updateParam("lift", { identifier: "audio_beat" })).toBe(
      "An audio slider's level, bass and beat already use that name.",
    )
    useEditor.getState().addParam({ identifier: "audio_bass", displayName: "Bass", type: "float", min: 0, max: 1, default: 0 })
    const r = compile(doc(), { mode: "export" })
    expect(r.ok).toBe(false)
    expect(r.errors[0].message).toContain('taken by the audio slider "audio"')
  })
})
