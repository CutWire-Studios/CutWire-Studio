import { describe, expect, it } from "vitest"
import { decodePeaks, legacyStockTarget, pageTitle, pruneLayout, resolveLink } from "./layout"

const sources = { "drift-assets": {}, sfx: {}, "stock.video": {}, "community.effect": {} }

describe("pruneLayout", () => {
  it("skips unknown section types and malformed sections", () => {
    const pages = pruneLayout(
      [
        {
          id: "home",
          title: "Home",
          sections: [
            { id: "a", type: "carousel-3d", title: "x" },
            { id: "b", type: "notice", text: "hi" },
            { id: "c", type: "notice" },
            "junk",
          ],
        },
      ],
      sources,
    )
    expect(pages[0]!.sections.map((s) => s.id)).toEqual(["b"])
  })

  it("drops sections whose source is missing, chips without a grid, and empty pages", () => {
    const pages = pruneLayout(
      [
        {
          id: "assets",
          title: "Assets",
          sections: [
            { id: "chips", type: "chips", target: "grid", source: { id: "drift-assets" } },
            { id: "grid", type: "grid", source: { id: "drift-assets" } },
            { id: "gone", type: "shelf", title: "Gone", source: { id: "stock.photo" } },
          ],
        },
        { id: "photo", title: "Photos", sections: [{ id: "browser", type: "stock-browser", source: { id: "stock.photo" } }] },
        { id: "orphan", title: "Orphan", sections: [{ id: "chips", type: "chips", target: "missing", source: { id: "sfx" } }] },
      ],
      sources,
    )
    expect(pages.map((p) => p.id)).toEqual(["assets"])
    expect(pages[0]!.sections.map((s) => s.id)).toEqual(["chips", "grid"])
  })

  it("removes see-all and hero actions that point at dropped pages", () => {
    const pages = pruneLayout(
      [
        {
          id: "home",
          title: "Home",
          sections: [
            { id: "hero", type: "hero", title: "Hi", action: { type: "open-page", page: "nope" } },
            { id: "shelf", type: "shelf", title: "S", source: { id: "community.effect" }, seeAll: { page: "nope" } },
            { id: "shelf2", type: "shelf", title: "S", source: { id: "community.effect" }, seeAll: { page: "home" } },
          ],
        },
      ],
      sources,
    )
    const [hero, shelf, shelf2] = pages[0]!.sections as unknown as { action?: unknown; seeAll?: unknown }[]
    expect(hero!.action).toBeUndefined()
    expect(shelf!.seeAll).toBeUndefined()
    expect(shelf2!.seeAll).toEqual({ page: "home" })
  })

  it("returns nothing for a non-array layout", () => {
    expect(pruneLayout({}, sources)).toEqual([])
  })
})

describe("pageTitle", () => {
  const page = { title: "Home", titles: { de: "Start", "pt-BR": "Inicio" } }
  it("prefers the exact tag, then the language, then the plain title", () => {
    expect(pageTitle(page, "pt-BR")).toBe("Inicio")
    expect(pageTitle(page, "de-AT")).toBe("Start")
    expect(pageTitle(page, "fr")).toBe("Home")
    expect(pageTitle({ title: "Home" }, "de")).toBe("Home")
  })
})

describe("decodePeaks", () => {
  it("maps 64 bytes to heights between 0 and 1", () => {
    const bytes = Uint8Array.from({ length: 64 }, (_, i) => (i === 63 ? 255 : i === 0 ? 0 : 51))
    const peaks = decodePeaks(btoa(String.fromCharCode(...bytes)))!
    expect(peaks).toHaveLength(64)
    expect(peaks[0]).toBe(0)
    expect(peaks[1]).toBeCloseTo(0.2)
    expect(peaks[63]).toBe(1)
  })
  it("returns null for missing or invalid data", () => {
    expect(decodePeaks(undefined)).toBeNull()
    expect(decodePeaks("")).toBeNull()
    expect(decodePeaks("%%%")).toBeNull()
  })
})

describe("resolveLink", () => {
  it("keeps same-origin links in the app, allows external https and refuses the rest", () => {
    const origin = "https://cutwire.studio"
    expect(resolveLink("https://cutwire.studio/community/effect/x?a=1", origin)).toEqual({ kind: "internal", path: "/community/effect/x?a=1" })
    expect(resolveLink("https://example.com/a", origin)).toEqual({ kind: "external", url: "https://example.com/a" })
    expect(resolveLink("http://example.com/a", origin).kind).toBe("blocked")
    expect(resolveLink("javascript:alert(1)", origin).kind).toBe("blocked")
  })
})

describe("legacyStockTarget", () => {
  it("maps old stock paths to marketplace pages", () => {
    expect(legacyStockTarget("/stock", "")).toBe("/marketplace")
    expect(legacyStockTarget("/stock/sfx", "")).toBe("/marketplace/sfx")
    expect(legacyStockTarget("/stock/drift-assets", "")).toBe("/marketplace/assets")
    expect(legacyStockTarget("/stock/search", "?type=music&q=calm")).toBe("/marketplace/music?q=calm")
    expect(legacyStockTarget("/stock/search", "?type=weird")).toBe("/marketplace")
    expect(legacyStockTarget("/stock/search", "")).toBe("/marketplace")
    expect(legacyStockTarget("/stock/item/pexels%3A1", "")).toBe("/marketplace")
  })
})
