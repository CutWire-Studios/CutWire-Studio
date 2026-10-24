import { ApiError } from "@cutwire/api-client"
import { describe, expect, it } from "vitest"
import { parseHandoff } from "./handoff"
import { checkMedia, coverRect, findQuality, isWebp, readWebpSize } from "./media"
import { paragraphs } from "./text"
import { parseBrowse, patchBrowse, toBrowseParams } from "./url-state"
import { firstStepWithViolations, groupByStep, stepForViolation, violationMessage, violationsOf } from "./violations"
import { applyVote, voteReducer } from "./vote"

describe("browse url state", () => {
  it("round-trips and omits defaults", () => {
    const state = parseBrowse(new URLSearchParams("kind=effect&q=glow&tags=a,b,a&category=color&sort=top"))
    expect(state).toEqual({ kind: "effect", q: "glow", tags: ["a", "b"], category: "color", sort: "top" })
    expect(parseBrowse(toBrowseParams(state))).toEqual(state)
    expect(toBrowseParams(parseBrowse(new URLSearchParams("sort=trending&q=%20"))).toString()).toBe("")
  })
  it("falls back for unknown sorts and resets vocabulary on kind change", () => {
    expect(parseBrowse(new URLSearchParams("sort=bogus")).sort).toBe("trending")
    const s = parseBrowse(new URLSearchParams("kind=effect&category=color&tags=a"))
    expect(patchBrowse(s, { kind: "theme" })).toMatchObject({ kind: "theme", category: "", tags: [] })
    expect(patchBrowse(s, { q: "x" })).toMatchObject({ category: "color", tags: ["a"] })
  })
})

describe("media spec check", () => {
  const spec = { width: 512, height: 288, maxBytes: 40_000 }
  it("accepts a conforming image", () => {
    expect(checkMedia({ isWebp: true, width: 512, height: 288, bytes: 39_000 }, spec)).toEqual([])
  })
  it("reports every problem", () => {
    const p = checkMedia({ isWebp: false, width: 500, height: 288, bytes: 50_000 }, spec).map((x) => x.code)
    expect(p).toEqual(["not_webp", "too_large", "wrong_dimensions"])
  })
  it("reads WebP magic and VP8X size", () => {
    const b = new Uint8Array(30)
    b.set([..."RIFF"].map((c) => c.charCodeAt(0)), 0)
    b.set([..."WEBP"].map((c) => c.charCodeAt(0)), 8)
    b.set([..."VP8X"].map((c) => c.charCodeAt(0)), 12)
    const w = 511
    const h = 287
    b.set([w & 255, (w >> 8) & 255, w >> 16, h & 255, (h >> 8) & 255, h >> 16], 24)
    expect(isWebp(b)).toBe(true)
    expect(readWebpSize(b)).toEqual({ width: 512, height: 288 })
    expect(isWebp(new Uint8Array(30))).toBe(false)
  })
})

describe("resize/encode helpers", () => {
  it("computes a centred cover crop", () => {
    expect(coverRect(1000, 1000, 512, 288)).toEqual({ sx: 0, sy: (1000 - 562.5) / 2, sw: 1000, sh: 562.5 })
    expect(coverRect(1024, 576, 512, 288, 2)).toMatchObject({ sw: 512, sh: 288, sx: 256, sy: 144 })
  })
  it("finds the highest quality under the cap", async () => {
    const size = (q: number) => Math.round(q * 100_000)
    const r = await findQuality(async (q) => size(q), 40_000)
    expect(r.fits).toBe(true)
    expect(r.bytes).toBeLessThanOrEqual(40_000)
    expect(r.quality).toBeGreaterThan(0.35)
    expect(await findQuality(async () => 10, 40_000)).toMatchObject({ quality: 0.95, fits: true })
    expect(await findQuality(async () => 90_000, 40_000)).toMatchObject({ quality: 0.05, fits: false })
  })
})

describe("violation mapping", () => {
  it("maps fields to steps", () => {
    expect(stepForViolation({ field: "package" })).toBe("package")
    expect(stepForViolation({ field: "title" })).toBe("details")
    expect(stepForViolation({ field: "tags" })).toBe("details")
    expect(stepForViolation({ field: "media.thumbnail" })).toBe("media")
    expect(stepForViolation({ field: "account" })).toBe("review")
    const vs = [
      { field: "media.thumbnail", code: "media_missing", message: "m" },
      { field: "title", code: "too_short", message: "t" },
    ]
    expect(groupByStep(vs).details).toHaveLength(1)
    expect(firstStepWithViolations(vs)).toBe("details")
    expect(firstStepWithViolations([])).toBeNull()
  })
  it("extracts violations from ApiError and formats counters", () => {
    const err = new ApiError(422, "content_rejected", "x", undefined, undefined, [{ field: "title", code: "too_short", message: "m" }])
    expect(violationsOf(err)).toHaveLength(1)
    expect(violationsOf(new ApiError(422, "c", "t", undefined, { title: ["a", "b"] }))).toHaveLength(2)
    expect(violationsOf(new Error("x"))).toEqual([])
    expect(violationMessage({ code: "not_english", message: "m", params: { ratio: 0.62, min: 0.75 } })).toBe("English words: 62% / 75% required.")
  })
})

describe("handoff parsing", () => {
  const blob = new Blob(["x"])
  it("parses the unified shape", () => {
    const h = parseHandoff({ kind: "transition", packageBlob: blob, filename: "a.driftfx", thumbnailBlob: blob, previewStripBlob: blob, title: "Nice wipe" })
    expect(h).toMatchObject({ kind: "transition", filename: "a.driftfx", title: "Nice wipe", key: "studio:publish-handoff" })
    expect(Object.keys(h!.media).sort()).toEqual(["preview-strip", "thumbnail"])
  })
  it("parses the Theme Maker v1 record", () => {
    const h = parseHandoff(
      { version: 1, kind: "theme", source: "themes", meta: { name: "Midnight" }, package: { fileName: "m.json", blob }, screenshot: { blob } },
      "theme-upload",
    )
    expect(h).toMatchObject({ kind: "theme", filename: "m.json", title: "Midnight", key: "theme-upload" })
    expect(h!.media.screenshot).toBe(blob)
  })
  it("rejects junk", () => {
    expect(parseHandoff(null)).toBeNull()
    expect(parseHandoff({ kind: "theme" })).toBeNull()
    expect(parseHandoff({ kind: "effect", packageBlob: "no" })).toBeNull()
  })
})

describe("optimistic vote", () => {
  it("adds, switches and clears votes", () => {
    const s0 = { score: 5, viewerVote: null }
    const up = applyVote(s0, 1)
    expect(up).toEqual({ score: 6, viewerVote: 1 })
    expect(applyVote(up, -1)).toEqual({ score: 4, viewerVote: -1 })
    expect(applyVote(up, 1)).toEqual(s0)
  })
  it("rolls back", () => {
    const s0 = { score: 1, viewerVote: null as 1 | -1 | null }
    const s1 = voteReducer(s0, { type: "click", value: -1 })
    expect(voteReducer(s1, { type: "rollback", to: s0 })).toEqual(s0)
  })
})

describe("description rendering", () => {
  it("splits paragraphs and linkifies without HTML", () => {
    const p = paragraphs("Hello <b>x</b> see https://a.example/x.\n\nSecond")
    expect(p).toHaveLength(2)
    expect(p[0]).toEqual([
      { type: "text", text: "Hello <b>x</b> see " },
      { type: "link", text: "https://a.example/x", href: "https://a.example/x" },
      { type: "text", text: "." },
    ])
  })
})
