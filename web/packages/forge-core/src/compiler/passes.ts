import type { SocketType } from "../doc/types"
import type { HelperName } from "../nodes/types"
import type { CompileError } from "./validate"

export type PassInput =
  | { type: "source_texture"; index?: number }
  | { type: "buffer"; id: string }
  | { type: "texture"; id: string }

export interface CompiledPass {
  file: string
  source: string
  inputs: PassInput[]
  output: { type: "canvas" } | { type: "buffer"; id: string }
  /**
   * Next Drift: parameters declared as sampler2D (image, track, trackMask, audio, gradient, curve), bound by name to
   * the texture units after `inputs`, in this order (see `paramSamplerUnit`).
   */
  paramSamplers: string[]
}

/** Drift binds depth on unit 8 and the clip mask on unit 9 in every pass, so param samplers skip them. */
export function paramSamplerUnit(inputCount: number, index: number): number {
  let unit = inputCount
  for (let i = 0; ; unit++) {
    if (unit === 8 || unit === 9) continue
    if (i++ === index) return unit
  }
}

export interface LiteralUniform {
  name: string
  node: string
  input: string
  type: SocketType
}

export interface CompiledTexture {
  id: string
  assetId: string
  file: string
}

export interface CompileResult {
  ok: boolean
  errors: CompileError[]
  passes: CompiledPass[]
  buffers: string[]
  textures: CompiledTexture[]
  /** Preview mode only: unconnected inputs become uniforms so sliders don't recompile. */
  literals: LiteralUniform[]
  usesTime: boolean
  /** Reads the clip's masks: the package declares "requires": "mask" and Drift adds the prelude. */
  usesMask?: boolean
  /** Reads depth: "requires": "depth" (next Drift). */
  usesDepth?: boolean
  /** Reads face anchors: "requires": "face" (next Drift). */
  usesFace?: boolean
}

export interface CompileOptions {
  mode: "export" | "preview"
  /** Render this node output instead of the Output node (per-node previews). */
  target?: { node: string; output: string }
}

export type TexRef = { kind: "source"; index: 0 | 1 } | { kind: "buffer"; sym: string } | { kind: "asset"; assetId: string }

export interface PassBuild {
  key: string
  writes: string | null
  samplers: TexRef[]
  helpers: Set<HelperName>
  functions: string[]
  fnNames: Map<string, string>
  usesRes: boolean
  usesTime: boolean
  usesProgress: boolean
  params: Set<string>
  engines: Set<string>
  literals: Map<string, LiteralUniform>
  main: string
}

export const samplerName = (i: number) => (i === 0 ? "u_currentTexture" : `u_texture${i}`)
