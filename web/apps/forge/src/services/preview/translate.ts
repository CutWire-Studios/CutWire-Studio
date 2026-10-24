// Mirrors drift::gl::translateShaderSource on OpenGL ES (VideoEd/src/engine/GlRuntime.cpp): the
// package's #version line is replaced and highp defaults are prepended, exactly as on Android.
export function esSource(source: string, fragment = true, prelude = ""): string {
  let body = source
  if (body.startsWith("#version")) {
    const nl = body.indexOf("\n")
    body = nl < 0 ? "" : body.slice(nl + 1)
  }
  let pre = "#version 300 es\nprecision highp float;\nprecision highp int;\n"
  if (fragment) pre += "precision highp sampler2D;\n"
  return pre + prelude + body
}

/**
 * Drift's prelude for "requires": "mask" packages (kMaskPrelude in GlRuntime.cpp), compiled in
 * before every pass. Package shaders call driftMask() without declaring it.
 */
export const MASK_PRELUDE = `
uniform sampler2D u_clipMask;
uniform float u_hasClipMask;

float driftMask(vec2 uv)
{
    return u_hasClipMask > 0.5 ? texture(u_clipMask, uv).r : 0.0;
}
`

/** Drift's prelude for "requires": "depth" packages (kDepthPrelude in GlRuntime.cpp). */
export const DEPTH_PRELUDE = `
uniform sampler2D u_depthTexture;
uniform vec2 u_depthResolution;
uniform float u_hasDepth;

float driftDepth(vec2 uv)
{
    return u_hasDepth > 0.5 ? texture(u_depthTexture, uv).r : 0.0;
}

float driftDepthGuided(vec2 uv, sampler2D guide)
{
    if (u_hasDepth < 0.5)
        return 0.0;
    vec2 texel = 1.0 / u_depthResolution;
    vec3 centre = texture(guide, uv).rgb;
    float sum = 0.0;
    float weights = 0.0;
    for (int y = -1; y <= 1; ++y) {
        for (int x = -1; x <= 1; ++x) {
            vec2 at = uv + vec2(float(x), float(y)) * texel;
            vec3 d = texture(guide, at).rgb - centre;
            float w = exp(-dot(d, d) * 40.0);
            sum += texture(u_depthTexture, at).r * w;
            weights += w;
        }
    }
    return sum / max(weights, 1e-4);
}

vec3 driftNormal(vec2 uv, float strength)
{
    if (u_hasDepth < 0.5)
        return vec3(0.0, 0.0, 1.0);
    vec2 t = 1.0 / u_depthResolution;
    float tl = texture(u_depthTexture, uv + vec2(-t.x, -t.y)).r;
    float tc = texture(u_depthTexture, uv + vec2(0.0, -t.y)).r;
    float tr = texture(u_depthTexture, uv + vec2(t.x, -t.y)).r;
    float ml = texture(u_depthTexture, uv + vec2(-t.x, 0.0)).r;
    float mr = texture(u_depthTexture, uv + vec2(t.x, 0.0)).r;
    float bl = texture(u_depthTexture, uv + vec2(-t.x, t.y)).r;
    float bc = texture(u_depthTexture, uv + vec2(0.0, t.y)).r;
    float br = texture(u_depthTexture, uv + vec2(t.x, t.y)).r;
    float gx = ((tr + 2.0 * mr + br) - (tl + 2.0 * ml + bl)) / 8.0 * u_depthResolution.x;
    float gy = ((bl + 2.0 * bc + br) - (tl + 2.0 * tc + tr)) / 8.0 * u_depthResolution.x;
    return normalize(vec3(-gx * strength, -gy * strength, 1.0));
}

vec2 packDepth(float d)
{
    float v = floor(clamp(d, 0.0, 1.0) * 65535.0 + 0.5);
    float high = floor(v / 256.0);
    return vec2(high, v - high * 256.0) / 255.0;
}

float unpackDepth(vec2 rg)
{
    vec2 bytes = floor(rg * 255.0 + 0.5);
    return (bytes.x * 256.0 + bytes.y) / 65535.0;
}
`

/** Desktop GLSL with the mask prelude spliced in after the version line, as Drift compiles it. */
export function withMaskPrelude(source: string): string {
  const nl = source.indexOf("\n")
  return source.startsWith("#version") ? source.slice(0, nl + 1) + MASK_PRELUDE + source.slice(nl + 1) : MASK_PRELUDE + source
}

/** The texture unit Drift binds the clip mask to (kClipMaskTextureUnit). */
export const CLIP_MASK_UNIT = 9

/** The texture unit Drift binds the depth map to (kDepthTextureUnit). */
export const DEPTH_UNIT = 8

export const QUAD_VERTEX_SHADER = `#version 330 core
layout(location = 0) in vec2 a_position;
layout(location = 1) in vec2 a_texCoord;
out vec2 v_texCoord;
void main() {
    v_texCoord = a_texCoord;
    gl_Position = vec4(a_position, 0.0, 1.0);
}
`
