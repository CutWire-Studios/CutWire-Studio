// Replays a fixed sequence of tool calls through the server bundle's entry (node) and compares with the fixture the
// backend's Jint tests replay too. Regenerate with `UPDATE_PARITY=1 pnpm --filter @cutwire/forge-core test`.
import { existsSync, readFileSync, writeFileSync } from "node:fs"
import { fileURLToPath } from "node:url"
import { expect, test } from "vitest"
import * as server from "./server-entry"

const FIXTURE = fileURLToPath(new URL("../../../../backend/tests/CutWire.Studio.UnitTests/Fixtures/forge-parity.json", import.meta.url))

interface Step {
  tool: string
  args: Record<string, unknown>
}

const EFFECT: Step[] = [
  { tool: "list_blocks", args: { category: "color" } },
  { tool: "describe_block", args: { types: ["blur", "brightness"] } },
  { tool: "add_block", args: { type: "blur" } },
  { tool: "view_graph", args: {} },
  { tool: "add_block", args: { type: "nope" } },
]

/** Starters get random ids when forge-core loads, so the inputs are pinned in the fixture. */
const pinned: Record<string, string> = existsSync(FIXTURE) && !process.env.UPDATE_PARITY ? JSON.parse(readFileSync(FIXTURE, "utf8")).inputs : {}

function run(kind: "effect" | "audio", steps: Step[], from?: string) {
  let doc = from ?? pinned[kind] ?? server.newDocument(kind, "Parity", null)
  const input = doc
  seed()
  const out: { tool: string; args: Step["args"]; output: server.ApplyResult }[] = []
  for (const s of steps) {
    const output = JSON.parse(server.applyTool(doc, s.tool, JSON.stringify(s.args))) as server.ApplyResult
    out.push({ ...s, output })
    if (output.doc) doc = output.doc
  }
  return { kind, input, steps: out, view: server.view(doc), shader: kind === "effect" ? server.shader(doc) : null, exportPlan: kind === "effect" ? server.exportPlan(doc) : null }
}

/** Same generator as the backend test's prelude, so random ids match between engines. */
function seed(): void {
  let a = 12345
  Math.random = () => {
    a = (a + 0x6d2b79f5) | 0
    let t = Math.imul(a ^ (a >>> 15), 1 | a)
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296
  }
}

function scenario() {
  const effect = run("effect", EFFECT)
  const first = JSON.parse(effect.steps[2].output.created ? JSON.stringify(effect.steps[2].output.created) : "[]")[0] as string
  const wired = run("effect", [
    ...EFFECT.slice(0, 3),
    { tool: "view_graph", args: {} },
    { tool: "set_setting", args: { block: first, setting: "radius", value: 4 } },
    { tool: "connect", args: { from: first, to: "output" } },
  ], effect.input)
  const audio = run("audio", [
    { tool: "list_pedals", args: {} },
    { tool: "add_pedal", args: { type: "delay" } },
    { tool: "view_rack", args: {} },
  ])
  const inputs = { effect: effect.input, audio: audio.input }
  return { inputs, effect, wired, audio }
}

test("server entry matches the backend fixture", () => {
  const current = JSON.parse(JSON.stringify(scenario()))
  if (process.env.UPDATE_PARITY || !existsSync(FIXTURE)) writeFileSync(FIXTURE, JSON.stringify(current, null, 1) + "\n")
  expect(current).toEqual(JSON.parse(readFileSync(FIXTURE, "utf8")))
})
