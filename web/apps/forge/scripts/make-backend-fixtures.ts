// Writes one effect, one transition and one audio effect .driftfx for the backend's parser tests.
//
//   pnpm --filter @cutwire/forge-app exec vite-node scripts/make-backend-fixtures.ts <dir>
import { writeFileSync } from "node:fs"
import { join } from "node:path"
import * as rack from "@cutwire/forge-core/audio/rack"
import { addNode, connect, isOpError, setInput, type OpResult } from "@cutwire/forge-core/edit/ops"
import type { ForgeDoc } from "@cutwire/forge-core/doc/types"
import { emptyDoc } from "@cutwire/forge-core/doc/util"
import { exportDriftfx } from "@cutwire/forge-core/export/archive"
import { outputType } from "@cutwire/forge-core/nodes/registry"

const out = process.argv[2]
if (!out) throw new Error("usage: make-backend-fixtures.ts <dir>")

function must<T extends object>(r: OpResult<T>): { doc: ForgeDoc } & T {
  if (isOpError(r)) throw new Error(r.error)
  return r
}

class Graph {
  doc: ForgeDoc
  constructor(kind: "effect" | "transition", id: string, name: string) {
    this.doc = emptyDoc(kind, name)
    this.doc.meta.id = id
    this.doc.meta.description = "A fixture for the backend parser tests."
  }
  block(type: string, inputs: Record<string, number | number[]> = {}): string {
    const r = must(addNode(this.doc, type, 0, 0))
    this.doc = r.doc
    for (const [k, v] of Object.entries(inputs)) this.doc = setInput(this.doc, r.id, k, v as never)
    return r.id
  }
  wire(from: string, o: string, to: string, input: string): void {
    this.doc = must(connect(this.doc, from, o, to, input)).doc
  }
}

const effect = new Graph("effect", "forge_fixture_tint", "Fixture tint")
{
  const video = effect.block("video")
  const mix = effect.block("mix", { b: [0.9, 0.3, 0.2, 1], amount: 0.4 })
  const o = effect.block(outputType("effect"))
  effect.wire(video, "image", mix, "a")
  effect.wire(mix, "image", o, "image")
}

const transition = new Graph("transition", "forge_fixture_wipe", "Fixture wipe")
{
  const from = transition.block("from")
  const to = transition.block("to")
  const mask = transition.block("wipe_mask")
  const mix = transition.block("mix")
  const o = transition.block(outputType("transition"))
  transition.wire(from, "image", mix, "a")
  transition.wire(to, "image", mix, "b")
  transition.wire(mask, "mask", mix, "amount")
  transition.wire(mix, "image", o, "image")
}

let audio = emptyDoc("audio", "Fixture filter")
audio.meta.id = "forge_fixture_filter"
{
  const filter = must(rack.addPedal(audio, "filter"))
  audio = filter.doc
}

for (const [name, doc] of [
  ["effect", effect.doc],
  ["transition", transition.doc],
  ["audio-effect", audio],
] as const) {
  const bytes = await exportDriftfx(doc, { png: null, irs: new Map() })
  writeFileSync(join(out, `${name}.driftfx`), bytes)
  console.log(`${name}.driftfx: ${bytes.length} bytes`)
}
