import { afterEach, describe, expect, it, vi } from "vitest"
import { listQuery, marketplace } from "./marketplace"

const json = (body: unknown, init: ResponseInit = {}) =>
  new Response(JSON.stringify(body), { status: 200, headers: { "Content-Type": "application/json" }, ...init })

const stubFetch = (res: Response) => {
  const f = vi.fn().mockImplementation(() => Promise.resolve(res.clone()))
  vi.stubGlobal("fetch", f)
  return f
}

afterEach(() => vi.unstubAllGlobals())

describe("listQuery", () => {
  it("omits empties and sends filters as plain keys", () => {
    expect(listQuery({})).toBe("")
    expect(listQuery({ q: "sea", cursor: "abc", limit: 24, provider: "*", filters: { orientation: "landscape", hd: "", cursor: "no" } })).toBe(
      "?q=sea&provider=*&cursor=abc&limit=24&orientation=landscape",
    )
  })
})

describe("marketplace client", () => {
  it("lists items for an encoded source id", async () => {
    const f = stubFetch(json({ items: [], nextCursor: null, quota: null }))
    await marketplace.listItems("stock.video", { category: "a/b", cursor: "next" })
    expect(f.mock.calls[0]![0]).toBe("/api/v1/marketplace/sources/stock.video/items?category=a%2Fb&cursor=next")
  })

  it("gets one item with both ids encoded", async () => {
    const f = stubFetch(json({}))
    await marketplace.getItem("sfx", "a b/c")
    expect(f.mock.calls[0]![0]).toBe("/api/v1/marketplace/sources/sfx/items/a%20b%2Fc")
  })

  it("returns the manifest with its etag, and notModified on 304", async () => {
    const manifest = { schema: 1, features: {}, sources: {}, pages: [] }
    let f = stubFetch(json(manifest, { headers: { ETag: 'W/"1"' } }))
    expect(await marketplace.getManifest()).toEqual({ notModified: false, manifest, etag: 'W/"1"' })
    expect((f.mock.calls[0]![1].headers as Headers).has("If-None-Match")).toBe(false)

    f = stubFetch(new Response(null, { status: 304 }))
    expect(await marketplace.getManifest('W/"1"')).toEqual({ notModified: true })
    expect((f.mock.calls[0]![1].headers as Headers).get("If-None-Match")).toBe('W/"1"')
  })

  it("throws a problem for manifest errors", async () => {
    stubFetch(json({ title: "No", code: "invalid_client" }, { status: 401 }))
    await expect(marketplace.getManifest()).rejects.toMatchObject({ status: 401, code: "invalid_client" })
  })

  it("posts resolve and install bodies and polls by id", async () => {
    const f = stubFetch(json({ id: "r1" }))
    await marketplace.resolve("https://example.com/v")
    await marketplace.pollResolve("r1")
    await marketplace.install("stock.video", "pexels:1", "hd")
    await marketplace.getInstall("stock_9")
    const [resolve, poll, install, status] = f.mock.calls
    expect(resolve![0]).toBe("/api/v1/marketplace/resolve")
    expect(JSON.parse(resolve![1].body)).toEqual({ url: "https://example.com/v" })
    expect(poll![0]).toBe("/api/v1/marketplace/resolve/r1")
    expect(install![0]).toBe("/api/v1/marketplace/installs")
    expect(JSON.parse(install![1].body)).toEqual({ source: "stock.video", itemId: "pexels:1", variantId: "hd" })
    expect(status![0]).toBe("/api/v1/marketplace/installs/stock_9")
  })
})
