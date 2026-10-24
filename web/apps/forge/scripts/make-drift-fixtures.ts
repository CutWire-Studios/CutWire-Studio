// Writes the Forge-exported packages Drift's tests install (tests/data/forge_fixture_*.driftfx), so
// the contract between Forge's exporter and Drift's loader is checked from Drift's side too.
//
//   npx vite-node scripts/make-drift-fixtures.ts <drift checkout>/tests/data
//
// Writes one package per fixture; the effect fixtures cover the track, track mask, audio, depth and face blocks.
import { readFileSync, writeFileSync } from "node:fs"
import { join } from "node:path"
import { irFiles } from "@cutwire/forge-core/audio/irs"
import * as rack from "@cutwire/forge-core/audio/rack"
import { addNode, connect, isOpError, setData, setInput, updateParam, type OpResult } from "@cutwire/forge-core/edit/ops"
import type { ForgeDoc, InputValue, SplitBlock } from "@cutwire/forge-core/doc/types"
import { emptyDoc } from "@cutwire/forge-core/doc/util"
import { exportDriftfx } from "@cutwire/forge-core/export/archive"
import { outputType } from "@cutwire/forge-core/nodes/registry"

const out = process.argv[2]
if (!out) throw new Error("usage: make-drift-fixtures.ts <drift tests/data dir>")

function must<T extends object>(r: OpResult<T>): { doc: ForgeDoc } & T {
  if (isOpError(r)) throw new Error(r.error)
  return r
}

/** A filter swept by an LFO, then a blend into a convolution room and a switchable delay. */
function rackFixture(): ForgeDoc {
  let doc = emptyDoc("audio", "Fixture rack")
  doc.meta.id = "forge_fixture_rack"
  const filter = must(rack.addPedal(doc, "filter"))
  doc = rack.setValue(filter.doc, { kind: "knob", item: filter.id, knob: "mode" }, 2)
  const tone = must(rack.exposeValue(doc, { kind: "knob", item: filter.id, knob: "cutoff" }))
  doc = must(updateParam(tone.doc, tone.param, { identifier: "tone", displayName: "Tone", default: 1500 })).doc
  const lfo = must(rack.addModulator(doc, "lfo"))
  doc = must(rack.addRoute(lfo.doc, lfo.id, filter.id, "cutoff", 0.3)).doc
  const env = must(rack.addModulator(doc, "envelope"))
  doc = rack.setModulatorSource(env.doc, env.id, filter.id)

  const split = must(rack.addSplit(doc, "parallel"))
  doc = must(rack.setCrossfade(split.doc, split.id, true)).doc
  const space = must(rack.exposeValue(doc, { kind: "blend", item: split.id }))
  doc = must(updateParam(space.doc, space.param, { identifier: "space", displayName: "Space", default: 0.4 })).doc
  const wet = (rack.findItem(rack.rackOf(doc), split.id)!.item as SplitBlock).lanes[1].id
  const room = must(rack.addPedal(doc, "convolution", { lane: wet }))
  doc = rack.setIr(room.doc, room.id, "builtin:room")
  const delay = must(rack.addPedal(doc, "delay", { lane: wet }))
  const off = must(rack.exposeValue(delay.doc, { kind: "bypass", item: delay.id }))
  doc = must(updateParam(off.doc, off.param, { identifier: "echo_off", displayName: "No echo" })).doc
  return doc
}

const fromDisk = async (name: string) => new Uint8Array(readFileSync(new URL(`../public/audio/ir/${name}.wav`, import.meta.url)))

/** Builds an effect graph one block at a time. */
class Graph {
  doc: ForgeDoc
  constructor(id: string, name: string) {
    this.doc = emptyDoc("effect", name)
    this.doc.meta.id = id
  }
  block(type: string, inputs: Record<string, InputValue> = {}, data: Record<string, unknown> = {}): string {
    const r = must(addNode(this.doc, type, 0, 0))
    this.doc = r.doc
    for (const [k, v] of Object.entries(inputs)) this.doc = setInput(this.doc, r.id, k, v)
    for (const [k, v] of Object.entries(data)) this.doc = setData(this.doc, r.id, k, v)
    return r.id
  }
  wire(from: string, out: string, to: string, input: string): void {
    this.doc = must(connect(this.doc, from, out, to, input)).doc
  }
}

/** The Other track's picture, shown where a second track's mask is. */
function trackMatteFixture(): ForgeDoc {
  const g = new Graph("forge_fixture_track_matte", "Fixture track matte")
  const video = g.block("video")
  const other = g.block("other_clip")
  const matte = g.block("track_mask")
  const mix = g.block("mix")
  const out = g.block(outputType("effect"))
  g.wire(video, "image", mix, "a")
  g.wire(other, "image", mix, "b")
  g.wire(matte, "mask", mix, "amount")
  g.wire(mix, "image", out, "image")
  return g.doc
}

/** Bars rising from the bottom, one per spectrum position across the frame. */
function audioBarsFixture(): ForgeDoc {
  const g = new Graph("forge_fixture_audio_bars", "Fixture audio bars")
  const video = g.block("video")
  const uv = g.block("uv")
  const audio = g.block("audio_track")
  const height = g.block("math", { a: 1 }, { op: "sub" })
  const bar = g.block("smoothstep")
  const mix = g.block("mix")
  const out = g.block(outputType("effect"))
  g.wire(uv, "x", audio, "x")
  g.wire(uv, "y", height, "b")
  g.wire(audio, "spectrum", bar, "x")
  g.wire(height, "value", bar, "low")
  g.wire(height, "value", bar, "high")
  g.wire(video, "image", mix, "a")
  g.wire(bar, "value", mix, "amount")
  g.wire(mix, "image", out, "image")
  return g.doc
}

/** Depth tints the near parts, then the Clip mask keeps the change to the subject. Needs mask and depth. */
function depthMaskFixture(): ForgeDoc {
  const g = new Graph("forge_fixture_depth_mask", "Fixture depth mask")
  const video = g.block("video")
  const depth = g.block("depth")
  const subject = g.block("clip_mask")
  const tint = g.block("mix")
  const keep = g.block("mix")
  const out = g.block(outputType("effect"))
  g.wire(video, "image", tint, "a")
  g.wire(depth, "normal", tint, "b")
  g.wire(depth, "depth", tint, "amount")
  g.wire(video, "image", keep, "a")
  g.wire(tint, "image", keep, "b")
  g.wire(subject, "mask", keep, "amount")
  g.wire(keep, "image", out, "image")
  return g.doc
}

/** A dark frame around the tracked face, sized from the face's half width. */
function faceFixture(): ForgeDoc {
  const g = new Graph("forge_fixture_face", "Fixture face")
  const video = g.block("video")
  const uv = g.block("uv")
  const face = g.block("face")
  const centre = g.block("split_point")
  const dx = g.block("math", {}, { op: "sub" })
  const dy = g.block("math", {}, { op: "sub" })
  const dist = g.block("math", {}, { op: "hypot" })
  const outside = g.block("smoothstep")
  const mix = g.block("mix", { b: [0, 0, 0, 1] })
  const out = g.block(outputType("effect"))
  g.wire(face, "center", centre, "p")
  g.wire(uv, "x", dx, "a")
  g.wire(centre, "x", dx, "b")
  g.wire(uv, "y", dy, "a")
  g.wire(centre, "y", dy, "b")
  g.wire(dx, "value", dist, "a")
  g.wire(dy, "value", dist, "b")
  g.wire(dist, "value", outside, "x")
  g.wire(face, "rx", outside, "low")
  g.wire(face, "rx", outside, "high")
  g.wire(video, "image", mix, "a")
  g.wire(outside, "value", mix, "amount")
  g.wire(mix, "image", out, "image")
  return g.doc
}

const fixtures: [string, ForgeDoc][] = [
  ["forge_fixture_rack", rackFixture()],
  ["forge_fixture_track_matte", trackMatteFixture()],
  ["forge_fixture_audio_bars", audioBarsFixture()],
  ["forge_fixture_depth_mask", depthMaskFixture()],
  ["forge_fixture_face", faceFixture()],
]
for (const [name, doc] of fixtures) {
  const bytes = await exportDriftfx(doc, { png: null, irs: await irFiles(doc, fromDisk) })
  writeFileSync(join(out, `${name}.driftfx`), bytes)
  console.log(`${name}.driftfx: ${bytes.length} bytes`)
}
