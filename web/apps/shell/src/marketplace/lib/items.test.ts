import { afterEach, describe, expect, it, vi } from "vitest"
import { feedFetcher, feedKey, feedParams } from "./items"
import { describeFailure, jobPatch } from "./downloads"
import { ApiError } from "@cutwire/api-client"

afterEach(() => vi.unstubAllGlobals())

describe("feedFetcher", () => {
  it("chains cursors across pages and keeps layout defaults and overrides", async () => {
    const urls: string[] = []
    const pages = [
      { items: [{ id: "1" }], nextCursor: "c2", quota: null },
      { items: [{ id: "2" }], nextCursor: null, quota: null },
    ]
    vi.stubGlobal(
      "fetch",
      vi.fn(async (url: string) => {
        urls.push(url)
        return new Response(JSON.stringify(pages[urls.length - 1]), { status: 200 })
      }),
    )
    const params = feedParams({ id: "community.effect", sort: "trending", category: "blur" }, { q: "glow", category: "" })
    expect(params).toMatchObject({ sort: "trending", category: "blur", q: "glow" })
    const next = feedFetcher("community.effect", params)
    const first = await next(null)
    const second = await next(first.nextCursor)
    expect(urls[0]).toBe("/api/v1/marketplace/sources/community.effect/items?category=blur&q=glow&sort=trending")
    expect(urls[1]).toBe("/api/v1/marketplace/sources/community.effect/items?category=blur&q=glow&sort=trending&cursor=c2")
    expect(second.nextCursor).toBeNull()
  })

  it("restarts the feed key when a parameter changes", () => {
    expect(feedKey("sfx", { q: "a" })).not.toBe(feedKey("sfx", { q: "b" }))
    expect(feedKey("sfx", { q: "a", cursor: "x" })).toBe(feedKey("sfx", { q: "a" }))
  })
})

describe("downloads", () => {
  it("maps job state onto an entry", () => {
    expect(jobPatch({ id: "i", source: "sfx", status: "ready", progress: 1, files: [{ role: "main", name: "a.wav", url: "/f", size: 1, sha256: null, mime: null }], license: null, error: null, minAppVersion: null })).toMatchObject({
      status: "ready",
      error: null,
    })
    expect(jobPatch({ id: "i", source: "stock.video", status: "failed", progress: 0, files: null, license: null, error: { code: "x", detail: "Broke" }, minAppVersion: null })).toMatchObject({
      status: "failed",
      error: "Broke",
      files: [],
    })
  })
  it("explains quota failures", () => {
    expect(describeFailure(new ApiError(429, "quota", "Too many", undefined, undefined, undefined, 600))).toBe("Download limit reached. Try again in 10 minutes.")
    expect(describeFailure(new Error("boom"))).toBe("boom")
  })
})
