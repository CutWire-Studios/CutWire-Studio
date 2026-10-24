import { glslFloat } from "../glsl/literals"
import type { NodeDef } from "./types"

export const controlNodes: NodeDef[] = [
  {
    type: "choice",
    label: "Dropdown",
    category: "input",
    description: "A list of named choices. Outputs 0 for the first, 1 for the second… Wire it into Pick by choice.",
    cheap: true,
    inputs: [{ id: "value", label: "Choice", type: "float", default: 0, min: 0, max: 7, widget: "choice", integer: true }],
    options: [{ id: "labels", label: "Choices", kind: "labels", default: ["First", "Second", "Third"] }],
    outputs: [{ id: "value", label: "Index", type: "float" }],
    emit: (c) => ({ value: `return floor(${c.in("value")} + 0.5);` }),
  },
  {
    type: "pick",
    label: "Pick by choice",
    category: "mix",
    description: "Shows one of up to four images, chosen by a number: 0 shows the first, 1 the second…",
    inputs: [
      { id: "which", label: "Choice", type: "float", default: 0, min: 0, max: 3, integer: true },
      { id: "a", label: "Choice 0", type: "color", default: [0, 0, 0, 1] },
      { id: "b", label: "Choice 1", type: "color", default: [0, 0, 0, 1] },
      { id: "c", label: "Choice 2", type: "color", default: [0, 0, 0, 1] },
      { id: "d", label: "Choice 3", type: "color", default: [0, 0, 0, 1] },
    ],
    outputs: [{ id: "image", label: "Image", type: "color" }],
    emit: (c) => ({
      image: `float w = floor(${c.in("which")} + 0.5);
    if (w < 0.5) return ${c.in("a")};
    if (w < 1.5) return ${c.in("b")};
    if (w < 2.5) return ${c.in("c")};
    return ${c.in("d")};`,
    }),
  },
  {
    type: "region",
    label: "Region",
    category: "input",
    description: "A box or oval on the frame. Use the mask to blur a face, spotlight something, or limit any effect to one area.",
    inputs: [{ id: "softness", label: "Edge softness", type: "float", default: 0.02, min: 0, max: 0.3 }],
    options: [
      { id: "rect", label: "Area", kind: "region", default: [0.3, 0.25, 0.4, 0.5] },
      {
        id: "shape",
        label: "Shape",
        kind: "select",
        options: [
          { value: "rect", label: "Box" },
          { value: "ellipse", label: "Oval" },
        ],
        default: "ellipse",
      },
      { id: "invert", label: "Everything outside", kind: "toggle", default: false },
    ],
    outputs: [
      { id: "mask", label: "Mask", type: "float" },
      { id: "center", label: "Centre", type: "vec2" },
      { id: "size", label: "Size", type: "vec2" },
    ],
    emit: (c) => {
      const exposed = c.optParam("rect")
      const r = c.opt<number[] | object>("rect")
      const lit = Array.isArray(r) ? r : [0.3, 0.25, 0.4, 0.5]
      const rect = exposed ? `pv_${exposed}()` : `vec4(${lit.map(glslFloat).join(", ")})`
      const ellipse = c.opt<string>("shape") !== "rect"
      return {
        mask: `vec4 rg = ${rect};
    vec2 half2 = max(rg.zw * 0.5, vec2(0.0001));
    vec2 ctr = rg.xy + half2;
    ${
      ellipse
        ? "float sd = (length((uv - ctr) / half2) - 1.0) * min(half2.x, half2.y);"
        : "vec2 q = abs(uv - ctr) - half2;\n    float sd = length(max(q, 0.0)) + min(max(q.x, q.y), 0.0);"
    }
    float soft = max(${c.in("softness")}, 0.0005);
    float m = 1.0 - smoothstep(-soft, soft, sd);
    return ${c.opt<boolean>("invert") ? "1.0 - m" : "m"};`,
        center: `vec4 rg = ${rect};
    return rg.xy + rg.zw * 0.5;`,
        size: `vec4 rg = ${rect};
    return rg.zw;`,
      }
    },
  },
  {
    type: "seed",
    label: "Random seed",
    category: "input",
    description: "A number that reshuffles randomness. Expose it so each clip can get its own random pattern.",
    cheap: true,
    inputs: [{ id: "seed", label: "Seed", type: "float", default: 0, min: 0, max: 1000, widget: "seed" }],
    outputs: [{ id: "value", label: "Seed", type: "float" }],
    emit: (c) => ({ value: `return ${c.in("seed")};` }),
  },
  {
    type: "other_clip",
    label: "Other track",
    category: "input",
    description:
      "Another track from the timeline, chosen in Drift. Shows what that track plays at this moment, so the effect can use a whole track. For track mattes, luma mattes and displacing one track with another.",
    next: true,
    cheap: true,
    inputs: [],
    outputs: [{ id: "image", label: "Image", type: "color" }],
    emit: (c) => ({ image: `return ${c.paramSampler("clip", "uv")};` }),
  },
  {
    type: "track_mask",
    label: "Track mask",
    category: "input",
    description: "The masks drawn on a track in Drift, read at this moment. 1 inside, 0 outside, as a matte for any effect.",
    kinds: ["effect"],
    next: true,
    cheap: true,
    inputs: [],
    outputs: [{ id: "mask", label: "Mask", type: "float" }],
    emit: (c) => ({ mask: `return ${c.paramSampler("mask", "uv")}.r;` }),
  },
  {
    type: "audio_track",
    label: "Audio track",
    category: "input",
    description:
      "The sound of an audio track in Drift, or the whole mix. Spectrum runs from low to high pitch across Position, 0 to 1. Wave is the waveform across Position, -1 to 1. Level, bass and beat follow the loudness and the beats, 0 to 1.",
    kinds: ["effect"],
    next: true,
    cheap: true,
    inputs: [{ id: "x", label: "Position", type: "float", default: 0.5, min: 0, max: 1 }],
    outputs: [
      { id: "spectrum", label: "Spectrum", type: "float" },
      { id: "wave", label: "Wave", type: "float" },
      { id: "level", label: "Loudness", type: "float" },
      { id: "bass", label: "Bass", type: "float" },
      { id: "beat", label: "Beat pulse", type: "float" },
    ],
    emit: (c) => {
      const audio = c.paramName("audio")
      const x = c.in("x")
      return {
        spectrum: `return ${c.paramSampler("audio", `vec2(${x}, 0.25)`)}.r;`,
        wave: `return ${c.paramSampler("audio", `vec2(${x}, 0.75)`)}.r * 2.0 - 1.0;`,
        level: `return ${c.engine(`${audio}_level`)};`,
        bass: `return ${c.engine(`${audio}_bass`)};`,
        beat: `return ${c.engine(`${audio}_beat`)};`,
      }
    },
  },
  {
    type: "depth",
    label: "Depth",
    category: "input",
    description:
      "How near each part of the clip is to the camera, from the depth estimate in Drift. Depth runs 0 far to 1 near. Normal is the surface direction as a colour (x, y, z times 0.5 plus 0.5). Has depth is 1 when the clip has an estimate.",
    kinds: ["effect"],
    next: true,
    inputs: [{ id: "strength", label: "Relief", type: "float", default: 1, min: 0, max: 4 }],
    outputs: [
      { id: "depth", label: "Depth", type: "float" },
      { id: "normal", label: "Normal", type: "color" },
      { id: "has", label: "Has depth", type: "float" },
    ],
    emit: (c) => ({
      depth: `return ${c.depth("uv")};`,
      normal: `return vec4(${c.depthNormal("uv", c.in("strength"))} * 0.5 + 0.5, 1.0);`,
      has: `return ${c.hasDepth()};`,
    }),
  },
  {
    type: "face",
    label: "Face",
    category: "input",
    description:
      "Where the tracked face sits on the clip, from Drift's face tracking. Positions are on the frame, 0 to 1; Face picks which person when there are several. Where there is no face, the frame passes through unchanged.",
    kinds: ["effect"],
    next: true,
    cheap: true,
    inputs: [],
    outputs: [
      { id: "leftEye", label: "Left eye", type: "vec2" },
      { id: "rightEye", label: "Right eye", type: "vec2" },
      { id: "nose", label: "Nose", type: "vec2" },
      { id: "mouth", label: "Mouth", type: "vec2" },
      { id: "chin", label: "Chin", type: "vec2" },
      { id: "forehead", label: "Forehead", type: "vec2" },
      { id: "center", label: "Centre", type: "vec2" },
      { id: "angle", label: "Tilt", type: "float" },
      { id: "rx", label: "Half width", type: "float" },
      { id: "ry", label: "Half height", type: "float" },
      { id: "valid", label: "Has a face", type: "float" },
    ],
    emit: (c) => {
      const at = (name: string) => `vec2(${c.face(`${name}X`)}, ${c.face(`${name}Y`)})`
      return {
        leftEye: `return ${at("u_faceLeftEye")};`,
        rightEye: `return ${at("u_faceRightEye")};`,
        nose: `return ${at("u_faceNose")};`,
        mouth: `return ${at("u_faceMouth")};`,
        chin: `return ${at("u_faceChin")};`,
        forehead: `return ${at("u_faceForehead")};`,
        center: `return ${at("u_faceCenter")};`,
        angle: `return ${c.face("u_faceAngle")};`,
        rx: `return ${c.face("u_faceRx")};`,
        ry: `return ${c.face("u_faceRy")};`,
        valid: `return ${c.face("u_faceValid")};`,
      }
    },
  },
  {
    type: "clip_mask",
    label: "Clip mask",
    category: "input",
    description:
      "The masks on the clip in Drift, such as Cut out subject: 1 inside, 0 outside. The effect takes the mask over, so Drift stops cutting the clip out with it. In a transition, the outgoing clip's mask.",
    next: true,
    cheap: true,
    inputs: [],
    outputs: [
      { id: "mask", label: "Mask", type: "float" },
      { id: "has", label: "Has a mask", type: "float" },
    ],
    emit: (c) => ({ mask: `return ${c.mask("uv")};`, has: `return ${c.hasMask()};` }),
  },
  {
    type: "audio",
    label: "Music",
    category: "input",
    description: "Follows the music: overall loudness, bass, and a pulse on every beat. Each runs 0 to 1.",
    next: true,
    cheap: true,
    inputs: [],
    outputs: [
      { id: "level", label: "Loudness", type: "float" },
      { id: "bass", label: "Bass", type: "float" },
      { id: "beat", label: "Beat pulse", type: "float" },
    ],
    emit: (c) => ({
      level: `return ${c.engine("u_audioLevel")};`,
      bass: `return ${c.engine("u_audioBass")};`,
      beat: `return ${c.engine("u_audioBeat")};`,
    }),
  },
]
