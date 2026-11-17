// Golden fixtures for the native C++ port of forge-core's compiler: for each case the document, the
// files compile(doc, { mode: "export" }) plus the manifest produce, and the preview-mode compile.
//
//   npx vite-node scripts/make-native-fixtures.ts <drift>/tests/data/forge
import { mkdirSync, writeFileSync } from "node:fs"
import { join } from "node:path"
import { compile, type CompileResult } from "@cutwire/forge-core/compiler/compile"
import { packageJson, packageJsonName } from "@cutwire/forge-core/compiler/manifest"
import { legacyAudioDoc } from "@cutwire/forge-core/audio/processors"
import type { ForgeDoc, Kind, ParamDef } from "@cutwire/forge-core/doc/types"
import { emptyDoc } from "@cutwire/forge-core/doc/util"
import { SAMPLER_NODES } from "@cutwire/forge-core/edit/graph"
import { availableFor, createNode, NODE_DEFS, nodeDef, outputType } from "@cutwire/forge-core/nodes/registry"
import { STARTERS } from "@cutwire/forge-core/starters"
import { AUDIO_STARTERS } from "@cutwire/forge-core/starters/audio"
import { structureKey } from "@/services/preview/bindings"

const out = process.argv[2]
if (!out) throw new Error("usage: make-native-fixtures.ts <dir>")

const PIXEL = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=="

/** JSON with keys sorted at every level, so reruns are byte-identical. */
function stable(v: unknown, indent = 2): string {
  const sort = (x: unknown): unknown => {
    if (Array.isArray(x)) return x.map(sort)
    if (x && typeof x === "object") {
      return Object.fromEntries(
        Object.keys(x)
          .sort()
          .map((k) => [k, sort((x as Record<string, unknown>)[k])]),
      )
    }
    return x
  }
  return JSON.stringify(sort(v), null, indent) + "\n"
}

/** Random node, edge and document ids become fixed ones so the fixtures don't change between runs. */
function canonical(doc: ForgeDoc, name: string): ForgeDoc {
  const d = structuredClone(doc)
  const ids = new Map(d.nodes.map((n, i) => [n.id, `n${i + 1}`]))
  for (const n of d.nodes) n.id = ids.get(n.id)!
  d.edges.forEach((e, i) => {
    e.id = `e${i + 1}`
    e.from = ids.get(e.from) ?? e.from
    e.to = ids.get(e.to) ?? e.to
  })
  d.meta.id = name
  return d
}

const cases: string[] = []
const skipped: string[] = []

function write(dir: string, file: string, text: string) {
  mkdirSync(dir, { recursive: true })
  writeFileSync(join(dir, file), text)
}

function previewJson(doc: ForgeDoc, r: CompileResult) {
  const nodeErrors: Record<string, string> = {}
  for (const e of r.errors) if (e.node) nodeErrors[e.node] = e.message
  return {
    ok: r.ok,
    structureKey: structureKey(doc),
    nodeErrors,
    errors: r.errors,
    passes: r.passes.map(({ source: _, ...p }) => p),
    buffers: r.buffers,
    textures: r.textures,
    literals: r.literals.map((l) => {
      const input = nodeDef(doc.nodes.find((n) => n.id === l.node)!.type)!.inputs.find((i) => i.id === l.input)!
      return { ...l, value: doc.nodes.find((n) => n.id === l.node)!.inputs[l.input] ?? input.default }
    }),
    usesTime: r.usesTime,
    usesMask: !!r.usesMask,
    usesDepth: !!r.usesDepth,
    usesFace: !!r.usesFace,
  }
}

/** Writes one case; returns the problem instead when the graph doesn't compile. */
function emit(name: string, source: ForgeDoc): string | null {
  const doc = canonical(source, name)
  const dir = join(out, name)
  if (doc.kind === "audio") {
    const r = compile(doc, { mode: "export" })
    if (!r.ok) return r.errors.map((e) => e.message).join("; ")
    const ids: string[] = []
    const collect = (v: unknown) => {
      if (Array.isArray(v)) v.forEach(collect)
      else if (v && typeof v === "object") {
        for (const [k, x] of Object.entries(v)) {
          if (k === "id" && typeof x === "string") ids.push(x)
          else collect(x)
        }
      }
    }
    collect(doc.audio)
    const fixed = (text: string) => [...new Set(ids)].reduce((t, id, i) => t.replaceAll(`"${id}"`, `"a${i + 1}"`), text)
    write(dir, "forge.json", fixed(stable(doc)))
    write(join(dir, "export"), packageJsonName(doc), fixed(stable(packageJson(doc, r))))
    cases.push(name)
    return null
  }
  const exported = compile(doc, { mode: "export" })
  const preview = compile(doc, { mode: "preview" })
  for (const r of [exported, preview]) if (!r.ok) return r.errors.map((e) => e.message).join("; ")
  write(dir, "forge.json", stable(doc))
  write(join(dir, "export"), packageJsonName(doc), stable(packageJson(doc, exported)))
  for (const p of exported.passes) write(join(dir, "export"), p.file, p.source)
  for (const p of preview.passes) write(join(dir, "preview"), p.file, p.source)
  write(join(dir, "preview"), "preview.json", stable(previewJson(doc, preview)))
  cases.push(name)
  return null
}

function emitOrFail(name: string, doc: ForgeDoc) {
  const problem = emit(name, doc)
  if (problem) throw new Error(`${name}: ${problem}`)
}

const slug = (s: string) =>
  s
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "_")
    .replace(/^_+|_+$/g, "")

mkdirSync(out, { recursive: true })

for (const s of STARTERS) emitOrFail(`starter_${s.doc.kind}_${slug(s.name)}`, s.doc)

const sourceNode = (kind: Kind) => createNode(kind === "transition" ? "from" : "video", 0, 0)

function pixelAsset(doc: ForgeDoc) {
  doc.assets.push({ id: "a1", name: "px.png", mime: "image/png", data: PIXEL, width: 1, height: 1 })
}

for (const def of NODE_DEFS) {
  const name = `node_${def.type}`
  if (def.output) {
    skipped.push(`${def.type}: output node, present in every other case`)
    continue
  }
  const kind: Kind = (["effect", "transition", "animation"] as const).find((k) => availableFor(def, k))!
  const doc = emptyDoc(kind)
  const sampler = SAMPLER_NODES[def.type]
  if (sampler) doc.params.push({ identifier: "src", displayName: sampler.label, type: sampler.type, min: 0, max: 1, default: "" } as ParamDef)
  pixelAsset(doc)
  const src = sourceNode(kind)
  const node = createNode(def.type, 200, 0)
  node.data.asset = "a1"
  if (sampler) node.data[sampler.key] = { param: "src" }
  const o = createNode(outputType(kind), 400, 0)
  doc.nodes.push(src, node, o)
  const colors = def.inputs.filter((i) => i.type === "color")
  if (colors[0]) doc.edges.push({ id: "e1", from: src.id, fromSocket: "image", to: node.id, toSocket: colors[0].id })
  if (kind === "transition" && colors[1]) {
    const to = createNode("to", 0, 100)
    doc.nodes.push(to)
    doc.edges.push({ id: "e3", from: to.id, fromSocket: "image", to: node.id, toSocket: colors[1].id })
  }
  const drive = def.outputs.find((x) => x.type === "color" || x.type === "float") ?? def.outputs[0]
  doc.edges.push({ id: "e2", from: node.id, fromSocket: drive.id, to: o.id, toSocket: "image" })
  const problem = emit(name, doc)
  if (problem) skipped.push(`${def.type}: ${problem}`)
}

function link(doc: ForgeDoc, from: string, fromSocket: string, to: string, toSocket: string) {
  doc.edges.push({ id: `${from}${to}${toSocket}`, from, fromSocket, to, toSocket })
}

const P = (identifier: string, type: ParamDef["type"], def: ParamDef["default"], extra: Partial<ParamDef> = {}): ParamDef =>
  ({ identifier, displayName: identifier, type, min: 0, max: 1, default: def, ...extra }) as ParamDef

// Every parameter type that drives a node input or option.
{
  const doc = emptyDoc("effect")
  pixelAsset(doc)
  doc.params.push(
    P("amount", "float", 0.4),
    P("flip", "bool", true),
    P("centre", "point", [0.5, 0.5]),
    P("slices", "int", 6, { max: 16 }),
    P("shuffle", "seed", 3, { max: 1000 }),
    P("tint", "color", "#ff000080", { alpha: true }),
    P("plain", "color", "#336699"),
    P("area", "region", [0.2, 0.2, 0.5, 0.5]),
    P("picture", "image", "a1"),
    P("ramp", "gradient", [{ pos: 0, color: [0, 0, 0, 1] }, { pos: 1, color: [1, 1, 1, 1] }]),
    P("motion", "curve", [{ x: 0, y: 0, ease: "smooth" }, { x: 1, y: 1, ease: "linear" }]),
    P("style", "choice", 1, { options: ["A", "B", "C"] }),
  )
  const v = createNode("video", 0, 0)
  const k = createNode("kaleidoscope", 0, 0)
  k.inputs.center = { param: "centre" }
  k.inputs.segments = { param: "slices" }
  const tint = createNode("tint", 0, 0)
  tint.inputs.color = { param: "tint" }
  tint.inputs.amount = { param: "amount" }
  const img = createNode("image", 0, 0)
  img.data.asset = { param: "picture" }
  const ramp = createNode("gradient_ramp", 0, 0)
  ramp.data.stops = { param: "ramp" }
  const curve = createNode("curve", 0, 0)
  curve.data.points = { param: "motion" }
  const region = createNode("region", 0, 0)
  region.data.rect = { param: "area" }
  const grain = createNode("grain", 0, 0)
  grain.inputs.seed = { param: "shuffle" }
  const pick = createNode("pick", 0, 0)
  pick.inputs.which = { param: "style" }
  const solid = createNode("color_value", 0, 0)
  solid.inputs.color = { param: "plain" }
  const sw = createNode("switch_value", 0, 0)
  sw.inputs.on = { param: "flip" }
  const mix = createNode("mix", 0, 0)
  const mix2 = createNode("mix", 0, 0)
  const o = createNode("effect_output", 0, 0)
  doc.nodes.push(v, k, tint, img, ramp, curve, region, grain, pick, solid, sw, mix, mix2, o)
  link(doc, v.id, "image", k.id, "image")
  link(doc, k.id, "image", tint.id, "image")
  link(doc, tint.id, "image", ramp.id, "image")
  link(doc, curve.id, "value", ramp.id, "amount")
  link(doc, ramp.id, "image", grain.id, "image")
  link(doc, grain.id, "image", pick.id, "a")
  link(doc, img.id, "image", pick.id, "b")
  link(doc, pick.id, "image", mix.id, "a")
  link(doc, solid.id, "color", mix.id, "b")
  link(doc, region.id, "mask", mix.id, "amount")
  link(doc, mix.id, "image", mix2.id, "a")
  link(doc, v.id, "image", mix2.id, "b")
  link(doc, sw.id, "value", mix2.id, "amount")
  link(doc, mix2.id, "image", o.id, "image")
  emitOrFail("params_all_types", doc)
}

// The three sampler parameters: another track, a track's mask lanes, an audio track.
{
  const doc = emptyDoc("effect")
  doc.params.push(
    { identifier: "other", displayName: "Other track", type: "track", min: 0, max: 1, default: "" } as ParamDef,
    { identifier: "matte", displayName: "Track mask", type: "trackMask", min: 0, max: 1, default: "" } as ParamDef,
    { identifier: "music", displayName: "Audio", type: "audio", min: 0, max: 1, default: "" } as ParamDef,
  )
  const v = createNode("video", 0, 0)
  const clip = createNode("other_clip", 0, 0)
  clip.data.clip = { param: "other" }
  const mask = createNode("track_mask", 0, 0)
  mask.data.mask = { param: "matte" }
  const audio = createNode("audio_track", 0, 0)
  audio.data.audio = { param: "music" }
  const mix = createNode("mix", 0, 0)
  const mix2 = createNode("mix", 0, 0)
  const o = createNode("effect_output", 0, 0)
  doc.nodes.push(v, clip, mask, audio, mix, mix2, o)
  link(doc, v.id, "image", mix.id, "a")
  link(doc, clip.id, "image", mix.id, "b")
  link(doc, mask.id, "mask", mix.id, "amount")
  link(doc, mix.id, "image", mix2.id, "a")
  link(doc, v.id, "image", mix2.id, "b")
  link(doc, audio.id, "level", mix2.id, "amount")
  link(doc, mix2.id, "image", o.id, "image")
  emitOrFail("params_tracks", doc)
}

function chainDoc(kind: Kind, types: string[], tweak: (n: ReturnType<typeof createNode>, i: number) => void = () => {}): ForgeDoc {
  const doc = emptyDoc(kind)
  const src = sourceNode(kind)
  doc.nodes.push(src)
  let prev = src
  let prevOut = "image"
  types.forEach((t, i) => {
    const n = createNode(t, 0, 0)
    tweak(n, i)
    doc.nodes.push(n)
    link(doc, prev.id, prevOut, n.id, nodeDef(t)!.inputs.find((x) => x.type === "color")!.id)
    prev = n
    prevOut = nodeDef(t)!.outputs[0].id
  })
  const o = createNode(outputType(kind), 0, 0)
  doc.nodes.push(o)
  link(doc, prev.id, prevOut, o.id, "image")
  return doc
}

emitOrFail("heavy_inputs_blur_of_noise", (() => {
  const doc = emptyDoc("effect")
  const noise = createNode("noise", 0, 0)
  const blur = createNode("blur", 0, 0)
  const o = createNode("effect_output", 0, 0)
  doc.nodes.push(noise, blur, o)
  link(doc, noise.id, "value", blur.id, "image")
  link(doc, blur.id, "image", o.id, "image")
  return doc
})())
emitOrFail("multi_stage_twirl_blur_glow", chainDoc("effect", ["twirl", "blur", "glow"]))
emitOrFail("buffer_reuse_three_blurs", chainDoc("effect", ["blur", "blur", "blur"]))
emitOrFail(
  "custom_glsl",
  chainDoc("effect", ["custom"], (n) => {
    n.data.code = "vec4 col = IN1(uv + vec2(a * 0.05, 0.0));\ncol.rgb = mix(col.rgb, vec3(luma(col)), b);\nreturn col;"
    n.data.heavy = true
  }),
)
emitOrFail("custom_glsl_transition_clock", chainDoc("transition", ["custom"]))

for (const s of AUDIO_STARTERS.slice(1, 3)) emitOrFail(`audio_${slug(s.name)}`, s.doc)
emitOrFail("audio_echo", legacyAudioDoc("echo"))

write(out, "SKIPPED.txt", skipped.length ? skipped.join("\n") + "\n" : "")
write(
  out,
  "README.md",
  `Generated by \`npx vite-node scripts/make-native-fixtures.ts <this dir>\` in web/apps/forge (CutWire-Studio). Do not edit.
Each case: forge.json (the document), export/ (compile mode "export" files and manifest), preview/ (compile mode "preview": pass sources and preview.json).
`,
)
console.log(`${cases.length} cases, ${skipped.length} skipped`)
