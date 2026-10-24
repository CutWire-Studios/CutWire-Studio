import { describe, expect, it } from "vitest"
import { buildHandoff, communityKind, HANDOFF_DB, HANDOFF_KEY, HANDOFF_STORE, uploadUrl } from "@/services/publish/handoff"
import { fitWebp, KIND_MEDIA } from "@/services/publish/webp"

/** A fake encoder whose output size grows with quality, like a real one. */
const sizer = (bytesAt: (q: number) => number) => {
  const tried: number[] = []
  return {
    tried,
    encode: async (q: number) => {
      tried.push(q)
      return new Blob([new Uint8Array(Math.round(bytesAt(q)))], { type: "image/webp" })
    },
  }
}

describe("webp quality search", () => {
  it("uses the best quality when it already fits", async () => {
    const e = sizer((q) => q * 10_000)
    const r = await fitWebp(e.encode, 40 * 1024)
    expect(r.fits).toBe(true)
    expect(r.quality).toBe(0.95)
    expect(e.tried).toEqual([0.95])
  })

  it("binary-searches the highest quality under the cap", async () => {
    const cap = 40 * 1024
    const e = sizer((q) => q * 100_000)
    const r = await fitWebp(e.encode, cap)
    expect(r.fits).toBe(true)
    expect(r.bytes).toBeLessThanOrEqual(cap)
    expect(r.bytes).toBe(r.blob.size)
    expect(r.quality).toBeGreaterThan(0.38)
    expect(r.quality).toBeLessThanOrEqual(0.4096)
    expect(e.tried.length).toBeLessThanOrEqual(9)
  })

  it("reports a failure when even the lowest quality is too big", async () => {
    const r = await fitWebp(sizer(() => 200_000).encode, KIND_MEDIA.strip.cap)
    expect(r.fits).toBe(false)
    expect(r.quality).toBe(0.05)
  })

  it("carries the exact community sizes and caps", () => {
    expect(KIND_MEDIA.thumbnail).toEqual({ width: 512, height: 288, cap: 40 * 1024 })
    expect(KIND_MEDIA.strip).toEqual({ width: 1536, height: 128, cap: 80 * 1024 })
  })
})

describe("publish handoff", () => {
  const thumbnail = new Blob(["t"], { type: "image/webp" })
  const previewStrip = new Blob(["s"], { type: "image/webp" })
  const base = { packageBytes: new Uint8Array([1, 2, 3]), filename: "glitch.driftfx", thumbnail, previewStrip, title: "Glitch", description: "A glitch." }

  it("uses the unified key and database", () => {
    expect([HANDOFF_DB, HANDOFF_STORE, HANDOFF_KEY]).toEqual(["cutwire-handoff", "handoff", "studio:publish-handoff"])
  })

  it("builds the unified record for a transition, with the strip", () => {
    const h = buildHandoff({ ...base, kind: "transition" })
    expect(h).toMatchObject({ kind: "transition", filename: "glitch.driftfx", title: "Glitch", description: "A glitch.", source: "forge" })
    expect(h.packageBlob).toBeInstanceOf(Blob)
    expect(h.packageBlob.size).toBe(3)
    expect(h.thumbnailBlob).toBe(thumbnail)
    expect(h.previewStripBlob).toBe(previewStrip)
  })

  it("leaves the strip out for effects and maps audio to audio-effect", () => {
    const fx = buildHandoff({ ...base, kind: "effect" })
    expect("previewStripBlob" in fx).toBe(false)
    const audio = buildHandoff({ ...base, kind: "audio" })
    expect(audio.kind).toBe("audio-effect")
    expect(uploadUrl(communityKind("audio"))).toBe("/community/upload?kind=audio-effect")
    expect(uploadUrl("transition")).toBe("/community/upload?kind=transition")
  })
})
