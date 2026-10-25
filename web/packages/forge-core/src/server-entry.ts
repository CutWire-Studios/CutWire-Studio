// Entry point of the bundle the .NET backend embeds and runs with Jint (`pnpm bundle:server`).
// Everything takes and returns JSON text, synchronously: no promises, no zstd, no hashing. The
// backend does compression, framing and sha256 itself.
import "./server-polyfills"
import { placeNew } from "./ai/layout"
import { systemPrompt } from "./ai/prompt"
import { fromOpenAI, toOpenAITools } from "./ai/providers/openaiFormat"
import { agentTools, BUILD_TOOLS, conversationTool, isShared, runTool, viewGraph } from "./ai/tools"
import { allItems, builtinIr, irPath, rackOf } from "./audio/rack"
import { compile } from "./compiler/compile"
import { minAppVersion, packageJson, packageJsonName } from "./compiler/manifest"
import { KIND_INFO } from "./doc/kinds"
import { imageParamFile } from "./doc/params"
import { isSplit, type ForgeDoc, type Kind } from "./doc/types"
import { makeEffectId } from "./doc/util"
import { LINK_LIMIT, LINK_PREFIX, linkDoc, parseForgeDoc } from "./export/link"
import { FORGE_FILE, GENERATOR, packageFiles, packageRoot } from "./export/package"
import { STARTERS } from "./starters"
import { AUDIO_STARTERS } from "./starters/audio"

const STARTER_LIST = [...STARTERS, ...AUDIO_STARTERS]
const doc = (json: string) => JSON.parse(json) as ForgeDoc
const asJson = (v: unknown) => JSON.stringify(v)

export const linkInfo = () => asJson({ prefix: LINK_PREFIX, limit: LINK_LIMIT })

/** Every build tool, as the MCP server and the chat offer them. */
export function tools(): string {
  return asJson(BUILD_TOOLS.map((t) => ({ name: t.spec.name, description: t.spec.description, parameters: t.spec.parameters, kinds: t.kinds, shared: isShared(t) })))
}

export function starters(): string {
  return asJson(STARTER_LIST.map((s) => ({ name: s.name, kind: s.doc.kind, description: s.description })))
}

export function newDocument(kind: Kind, name: string | null, starter: string | null): string {
  const pick =
    STARTER_LIST.find((s) => s.doc.kind === kind && s.name.toLowerCase() === (starter ?? "").toLowerCase()) ??
    STARTER_LIST.find((s) => s.doc.kind === kind && s.name.startsWith("Blank"))!
  const d = structuredClone(pick.doc)
  if (name) d.meta.displayName = name.slice(0, 60)
  d.meta.id = makeEffectId(d.meta.displayName)
  return asJson(d)
}

/** Validates and upgrades a document from forge.json, a link or storage; returns it as JSON. */
export const parseDoc = (text: string) => asJson(parseForgeDoc(text))

export const view = (json: string) => viewGraph(doc(json))

export interface ApplyResult {
  /** only when the call changed the document */
  doc?: string
  result: string
  error?: boolean
  created?: string[]
}

/** One build tool call; blocks it created are placed next to what feeds them. */
export function applyTool(json: string, name: string, argsJson: string): string {
  const d = doc(json)
  const r = runTool(d, name, JSON.parse(argsJson || "{}"))
  const out: ApplyResult = { result: r.result, error: r.error || undefined, created: r.created }
  if (r.doc !== d) out.doc = asJson(r.created?.length ? placeNew(r.doc, new Set(r.created)) : r.doc)
  return asJson(out)
}

/** A call from the built-in assistant: build tools plus the ones that wait for the user. */
export function agentCall(json: string, name: string, argsJson: string): string {
  const conversation = conversationTool(name)
  if (conversation) {
    const p = conversation.pending(JSON.parse(argsJson || "{}"))
    if ("error" in p) return asJson({ result: p.error, error: true })
    return asJson({ result: "Shown to the user. Wait for their answer.", pending: p })
  }
  const d = doc(json)
  const r = runTool(d, name, JSON.parse(argsJson || "{}"))
  return asJson({ doc: r.doc !== d ? asJson(r.doc) : undefined, result: r.result, error: r.error || undefined, created: r.created })
}

const DOC_NOUN: Record<Kind, string> = { effect: "graph", transition: "graph", audio: "board" }

/** What a hosted AI run starts a step with: system prompt, tool list (OpenAI format), the first user turn. */
export function agentSetup(json: string, prompt: string): string {
  const d = doc(json)
  return asJson({
    system: systemPrompt(d),
    tools: toOpenAITools(agentTools(d.kind)),
    first: `Current ${DOC_NOUN[d.kind]}:\n${viewGraph(d)}\n\nRequest: ${prompt}`,
  })
}

export const agentSystem = (json: string) => systemPrompt(doc(json))

export const place = (json: string, created: string[]) => asJson(placeNew(doc(json), new Set(created)))

/** A chat-completions answer as the agent sees it (text, reasoning, tool calls, usage). */
export const parseCompletion = (completionJson: string) => asJson(fromOpenAI(JSON.parse(completionJson)))

export function shader(json: string): string {
  const d = doc(json)
  const r = compile(d, { mode: "export" })
  if (!r.ok) return asJson({ ok: false, text: `problems: ${r.errors.map((e) => e.message).join(" | ")}` })
  const text = [`// ${packageJsonName(d)}`, JSON.stringify(packageJson(d, r), null, 2), ...r.passes.flatMap((p) => [`// ${p.file}`, p.source])].join("\n\n")
  return asJson({ ok: true, text })
}

/** The share-link payload before compression: the document without what Forge regenerates on open. */
export const linkText = (json: string) => JSON.stringify(linkDoc(doc(json)))

export interface ExportFile {
  path: string
  /** text files */
  text?: string
  /** binary files: a document asset by id, or one of the built-in impulse responses by name */
  asset?: string
  builtinIr?: string
  /** forge.json: the caller writes the document with its assets inline */
  forge?: boolean
}

/**
 * The files and manifest of a .driftfx for `json`. Binary content (textures, impulse responses)
 * is referenced, not embedded: the caller supplies the bytes. Throws when the document doesn't compile.
 */
export function exportPlan(json: string): string {
  const d = doc(json)
  const binary = new Map<string, Pick<ExportFile, "asset" | "builtinIr">>()
  const compiled = compile(d, { mode: "export" })
  if (compiled.ok) {
    for (const t of compiled.textures) binary.set(t.file, { asset: t.assetId })
    for (const p of d.params) {
      const file = p.type === "image" ? imageParamFile(d, p) : null
      if (file) binary.set(file, { asset: p.default as string })
    }
  }
  const irs = new Map<string, Uint8Array>()
  if (d.kind === "audio") {
    for (const item of allItems(rackOf(d))) {
      if (isSplit(item) || !item.ir) continue
      const path = irPath(d, item.ir)
      const builtin = builtinIr(item.ir)
      binary.set(path, builtin !== null ? { builtinIr: builtin } : { asset: item.ir })
      irs.set(path, new Uint8Array(0))
    }
  }
  const stripped: ForgeDoc = { ...d, assets: d.assets.map((a) => ({ ...a, data: "" })) }
  const { files, compiled: c } = packageFiles(stripped, { png: null, irs })
  const root = packageRoot(d)
  const dec = new TextDecoder()
  const out: ExportFile[] = files.map((f) => {
    const path = `${root}/${d.meta.id}/${f.path}`
    if (f.path === FORGE_FILE) return { path, forge: true }
    const ref = binary.get(f.path)
    return { path, ...(ref ?? { text: dec.decode(f.data) }) }
  })
  const base = {
    id: `user.${d.meta.id}`,
    version: d.meta.version || "1.0.0",
    name: d.meta.displayName,
    description: d.meta.description,
    details: `Made with Drift Forge. ${KIND_INFO[d.kind].label} id: ${d.meta.id}.`,
    author: d.meta.author,
    license: "",
    minAppVersion: minAppVersion(d, c),
    provides: [{ kind: root, root, items: 1 }],
    generator: GENERATOR,
  }
  return asJson({ base, files: out })
}
