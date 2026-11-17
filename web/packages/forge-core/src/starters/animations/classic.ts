import { animation, float, type Starter } from "../builder"

export const classicAnimations: Starter[] = [
  animation("Fade & rise", "basic", "The clip fades in while drifting up into place, and back down on the way out.", (g) => {
    const v = g.add("video", 0, 0)
    const d = g.add("direction", 0, 1)
    const lift = g.add("mix_value", 1, 1, { a: g.param(float("rise", "Rise", 0.12, 0, 0.5)), b: 0 })
    const pt = g.add("combine_point", 2, 1, { x: 0 })
    const t = g.add("transform", 3, 0, {}, { edge: "transparent" })
    const fade = g.add("set_alpha", 4, 0)
    const o = g.output(5)
    g.link(d, "settled", lift, "t")
    g.link(lift, "value", pt, "y")
    g.link(v, "image", t, "image")
    g.link(pt, "p", t, "offset")
    g.link(t, "image", fade, "image")
    g.link(d, "settled", fade, "mask")
    g.link(fade, "image", o, "image")
  }),

  animation("Dissolve", "reveal", "The clip emerges from, or melts into, drifting noise.", (g) => {
    const v = g.add("video", 0, 0)
    const n = g.add("noise", 0, 2, { speed: 0, scale: g.param(float("scale", "Patch size", 5, 0.5, 40)) })
    const r = g.add("reveal", 1, 1, { softness: g.param(float("softness", "Softness", 0.1, 0, 0.5)) })
    const cut = g.add("set_alpha", 2, 0)
    const o = g.output(3)
    g.link(n, "value", r, "pattern")
    g.link(v, "image", cut, "image")
    g.link(r, "mask", cut, "mask")
    g.link(cut, "image", o, "image")
  }),

  animation("Wipe reveal", "reveal", "A soft edge sweeps across to uncover the clip, or cover it again.", (g) => {
    const v = g.add("video", 0, 0)
    const w = g.add("wipe_mask", 1, 1, {
      angle: g.param(float("angle", "Direction", 0, 0, 360)),
      softness: g.param(float("softness", "Softness", 0.08, 0, 0.5)),
    })
    const cut = g.add("set_alpha", 2, 0)
    const o = g.output(3)
    g.link(v, "image", cut, "image")
    g.link(w, "mask", cut, "mask")
    g.link(cut, "image", o, "image")
  }),
]
