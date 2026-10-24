# Next-Drift parameters

Drift Forge exports controls that today's Drift doesn't understand whenever an effect uses them
(there is no switch: every control and block is always available). This file is the contract for implementing them in Drift
(`GpuPackageParse`, `setPackageUniforms`/`runPipeline` in `GlRuntime.cpp`, and the inspector).

Today's Drift refuses these packages, because an unknown parameter `type` is a parse error
(`GpuPackageParse.cpp:140-160`). Every package that uses them lists what it needs in a top-level
`"nextFeatures"` array, e.g. `["param:point", "param:gradient", "uniform:audio"]`, so a future
Drift can refuse with a clear message if it only implements some of them.

## Versions

Packages that list any of these in `nextFeatures` get `minAppVersion` **0.9.0**. Drift 0.8.0 can't
read them:

- `param:track`
- `param:trackMask`
- `param:audio`
- `requires:depth`
- `requires:face`

`node:*` entries and `requires:mask` don't raise the version. Forge's `forge.json` schema 3 renamed
the `clip` parameter type to `track`; schema 2 files load with the rename applied.

## Always safe (written for every target)

Today's parser reads only the keys it knows, so these are written into every export.

| Where | Key | Meaning |
| --- | --- | --- |
| parameter | `ui.control` | `"angle"` shows a dial; `"seed"` shows a number with a shuffle button |
| parameter | `ui.unit` | suffix after the value: `"px"`, `"°"`, `"%"`, `"s"` |
| parameter | `ui.step` | snap step; `1` for counts |
| parameter | `ui.precision` | decimals shown |
| parameter | `showWhen` | `{ "param": "<bool param id>", "equals": true }`: hide unless that switch matches |
| top level | `presets` | `[{ "name": "Subtle", "values": { "<id>": <value in that param's JSON form> } }]` |

## Parameter types

All parameters are still bound by uniform name = `identifier`, as today.

| `type` | JSON | GLSL uniform | Inspector |
| --- | --- | --- | --- |
| `int` | `minValue`, `maxValue`, `defaultValue` (whole numbers) | `float` holding a whole number | integer slider; keyframable |
| `point` | `minValue: [x, y]`, `maxValue: [x, y]`, `defaultValue: [x, y]` (0..1 of the frame, y down) | `vec2` | draggable handle on the preview; keyframable |
| `choice` | `options: ["Left", "Right", …]`, `defaultValue: index` | `float` index | dropdown |
| `seed` | `defaultValue` | `float` | number + shuffle |
| `color` + `"alpha": true` | `defaultValue: "#rrggbbaa"` | `vec4` (straight alpha) | swatch with opacity |
| `region` | `shape: "rect"\|"ellipse"`, `defaultValue: [x, y, w, h]` (0..1 of the frame, y down) | `vec4` | box/oval drawn on the preview |
| `image` | `defaultValue: "param_<id>.png"` (file in the package) | `sampler2D` | picture picker |
| `track` | (no default; a track id, `""` = none) | `sampler2D` | track picker |
| `trackMask` | (no default; a track id) | `sampler2D` | track picker (tracks with masks) |
| `audio` | (no default; a track id, `""` = Timeline mix) | `sampler2D` + `float` scalars | track picker |
| `gradient` | `defaultValue: [{ "position": 0..1, "color": "#rrggbbaa" }, …]` | `sampler2D` (256×1) | gradient editor |
| `curve` | `defaultValue: [{ "x": 0..1, "y": 0..1, "ease": "linear"\|"smooth"\|"in"\|"out"\|"hold" }, …]` | `sampler2D` (256×1, value in `.r`) | curve editor |

### Sampler parameters (`image`, `track`, `trackMask`, `audio`, `gradient`, `curve`)

- A pass that declares `uniform sampler2D <identifier>;` gets it bound by Drift. Use the texture
  units **after** the pass's `inputs` (unit `inputs.length`, then `+1`…), in the order the
  sampler parameters the pass uses appear in `parameters`.
- Units **8** and **9** are reserved in every pass: 8 holds depth and 9 the clip mask (see below),
  so sampler parameters skip them. With one input, the units are 1–7, then 10, 11, …
  (`paramSamplerUnit` in `src/core/compiler/passes.ts`).
- `image`, `track` and `trackMask` textures are oriented like clip frames and `pipeline.textures`:
  row 0 at v = 0 (the top).
- `image`: CLAMP_TO_EDGE, LINEAR. The default file is resolved relative to the package folder.
- `track`, `trackMask`: see the sections below. `audio`: see Shared audio contract.
- `gradient`/`curve`: rebuild a 256×1 RGBA8 texture whenever the value changes. Texel `i` holds the
  value at `t = i / 255`. Evaluate exactly like Forge: stops/keys sorted by position; before the
  first or after the last, hold that end's value; between two keys, interpolate linearly after
  applying the *left* key's ease to the 0..1 fraction (`smooth` = k²(3−2k), `in` = k³,
  `out` = 1−(1−k)³, `hold` = 0). Curves store y in the red channel, clamped to 0..1; shaders
  sample at `t * (255/256) + 0.5/256`.

### Track (`track`)

Written by the Other track block. The value is a track id; `""` means none.

- Drift renders the chosen track at the same timeline time T as a canvas-sized sampler, the same
  way a transition's inputs are: its own transform, effects and masks.
- The clip used is the one on that track containing T. During a transition on that track, only the
  outgoing clip is used (v1).
- Hidden tracks still feed it. The source track still draws normally.
- The track picker leaves out the host's own track.
- Resolved one level deep: a track input's own `track` and `trackMask` parameters get transparent
  textures, which avoids cycles.
- Transparent when the value is empty or the track has no clip at T.

### Track mask (`trackMask`)

Written by the Track mask block. The value is a track id; `""` means none.

- The chosen track's folded mask lanes at T, laid out on the canvas, the same fold as `driftMask()`
  (see Clip mask): coverage in `.r`, 0 when the track has no masks.
- Forge's Track mask block reads `texture(id, uv).r`.

### Audio (`audio`)

Written by the Audio track block. The value is a track id; `""` means the Timeline mix.

- Binds the 512×2 texture from Shared audio contract as a sampler named after the parameter, and
  three `float` uniforms: `<id>_level`, `<id>_bass` and `<id>_beat`.
- Forge rejects a parameter named like an audio parameter's `_level`, `_bass` or `_beat` scalar, so
  those names never collide.
- Forge's Audio track block reads row 0 at `vec2(x, 0.25)` (spectrum) and row 1 at `vec2(x, 0.75)`
  (waveform), then the three scalars.

### Keyframing

`int`, `point` and alpha `color` should be keyframable (a point as two tracks).

## Shared audio contract

The `audio` parameter, the Engine uniforms and Forge's preview all follow this. Drift's
`docs/gpu-effects.md` carries the same definition.

Audio analysis is a pure function of the timeline time T, never of playback state, so preview,
scrubbing and export agree.

**Analysis window:**
- 2048 mono samples at 48 kHz, centred on T.
- Mono is (L+R)/2 of the source after clip gain and fades. Track volume and pan are ignored.
- For a track, the window sums the track's clips that overlap it. For the Timeline mix, it is the
  mixed timeline audio.

**Texture:** 512×2 RGBA8, LINEAR and CLAMP. Row 0 is at v = 0. Shaders sample row 0 at `v = 0.25`
and row 1 at `v = 0.75`.

| Row | Content | Encoding |
| --- | --- | --- |
| 0: spectrum | Hann window, real FFT. 512 bins log-spaced from 20 Hz to 20 kHz. Each bin takes the max magnitude of the FFT bins it covers. | dBFS mapped from [-90, -10] to [0, 1], stored in `.r` |
| 1: waveform | The 2048 samples decimated to 512 points (mean of each group of 4) | Mapped from [-1, 1] to [0, 1], stored in `.r` |

**Scalars:**

| Scalar | Definition |
| --- | --- |
| `level` | RMS over the window, in dBFS, mapped from [-60, 0] to [0, 1] |
| `bass` | Energy below 150 Hz, in dBFS, mapped from [-60, 0] to [0, 1] |
| `beat` | `exp(-(T - lastOnset) / 0.09 s)`, using onsets from `AudioOnsets::analyze` for that source. 0 before the first onset. |

Onsets are cached per source and project edit. Preview computes them off-thread, so `beat` stays 0
until they are ready. Export computes them synchronously.

**Forge preview:** the preview builds both rows from an AnalyserNode with `fftSize = 2048` and
`smoothingTimeConstant = 0`, using `getFloatFrequencyData` and `getFloatTimeDomainData`, with the same
log resample and mapping. Two differences from Drift follow: the AnalyserNode uses a Blackman window,
not Hann, and it analyses the audio playing at that moment rather than a window centred on T. Expect
previews to differ slightly from Drift. Beat uses the existing onset follower, not `AudioOnsets`.

## Engine uniforms

Bound when a shader declares them; 0 when there's no audio.

| Uniform | Meaning |
| --- | --- |
| `u_audioLevel` | `level` for the Timeline mix (Shared audio contract), 0..1 |
| `u_audioBass` | `bass` for the Timeline mix, 0..1 |
| `u_audioBeat` | `beat` for the Timeline mix, 0..1; decays with a 0.09 s time constant after each onset |

These are the same scalars an `audio` parameter with an empty value gets. Transitions may read these
too: they are a function of timeline time, not of `u_time`, so they don't break the determinism rule
for transitions. Forge's Music block reads them, and the preview computes them as described above.

## Clip mask (`"requires": "mask"`)

Written when a graph uses the Clip mask block; listed in `nextFeatures` as `requires:mask`. Drift
implements it (`kMaskPrelude` in `GlRuntime.cpp`, see Drift's `docs/gpu-effects.md`, "Mask
effects"). Drift compiles a prelude into every pass, so package shaders call these without
declaring them:

| Name | Meaning |
| --- | --- |
| `float driftMask(vec2 uv)` | coverage of every mask pinned to the clip (Cut out subject, shapes), folded with feather, invert and the combine ops: 1 inside, 0 outside, 0 everywhere without a mask |
| `u_hasClipMask` | 1 when the clip has a mask, else 0 |
| `u_clipMask` | the folded coverage in `.r`, on texture unit 9 |

The package takes the masks over: the clip is drawn whole rather than cut out, and a cutout's
decontaminated foreground is not used. A transition reads the outgoing clip's masks, laid out on
the canvas. Forge's preview binds the Clip mask preview source (a sample clip's matte) in their
place and splices the same prelude in (`MASK_PRELUDE` in `src/services/preview/translate.ts`).

## Depth (`"requires": "depth"`)

Written when a graph uses the Depth block; listed in `nextFeatures` as `requires:depth`. The clip's
depth estimate is bound on texture unit 8 (`u_depthTexture`, with `u_depthResolution`). Drift compiles
this prelude into every pass (`kDepthPrelude` in `GlRuntime.cpp`), the same way as the mask prelude:

| Name | Meaning |
| --- | --- |
| `float driftDepth(vec2 uv)` | depth at `uv`, 0 far to 1 near; 0 when there is no estimate |
| `float driftDepthGuided(vec2 uv, sampler2D guide)` | depth at `uv` smoothed over a 3×3 neighbourhood, weighted by how alike the guide's colours are, so edges in the guide stay sharp; 0 when there is no estimate. Forge's blocks don't call it yet |
| `vec3 driftNormal(vec2 uv, float strength)` | surface normal from the depth, with `strength` scaling the relief; flat `(0, 0, 1)` when there is no estimate |
| `u_hasDepth` | 1 when the clip has an estimate, else 0 |

Forge's Depth block takes Relief (0..4, default 1) and outputs `depth` (`driftDepth(uv)`), `normal`
(`driftNormal(uv, relief) * 0.5 + 0.5`) and `has` (`u_hasDepth`).

## Face (`"requires": "face"`)

Written when a graph uses the Face block; listed in `nextFeatures` as `requires:face`. Forge also adds
a `faceIndex` float parameter (0..3, step 1, default 0) to choose the face when several are tracked.

The face anchors are bound as float uniforms, in the same space as the `uv` block (0,0 top-left):

| Uniform | Meaning |
| --- | --- |
| `u_faceLeftEyeX`, `u_faceLeftEyeY` | left eye |
| `u_faceRightEyeX`, `u_faceRightEyeY` | right eye |
| `u_faceNoseX`, `u_faceNoseY` | nose |
| `u_faceMouthX`, `u_faceMouthY` | mouth |
| `u_faceChinX`, `u_faceChinY` | chin |
| `u_faceForeheadX`, `u_faceForeheadY` | forehead |
| `u_faceCenterX`, `u_faceCenterY` | centre of the face |
| `u_faceAngle` | tilt of the face |
| `u_faceRx`, `u_faceRy` | half width and half height of the face |
| `u_faceValid` | 1 when the chosen face is tracked on this frame, else 0 |

When `u_faceValid < 0.5`, Forge's root pass outputs the source frame unchanged, so a clip with no face
passes through untouched. The source is always bound for that pass.
