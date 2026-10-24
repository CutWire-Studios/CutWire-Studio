import { describe, expect, it } from "vitest"
import { apps, getApp, tiles, tilesByCategory } from "./registry"

describe("registry", () => {
  it("has unique app ids and paths", () => {
    expect(new Set(apps.map((a) => a.id)).size).toBe(apps.length)
    expect(new Set(apps.map((a) => a.path)).size).toBe(apps.length)
  })

  it("uses root-relative paths without trailing slash", () => {
    for (const a of apps) expect(a.path).toMatch(/^\/[a-z]+$/)
  })

  it("has unique tile ids and every tile belongs to its app's path", () => {
    expect(new Set(tiles.map((t) => t.id)).size).toBe(tiles.length)
    for (const t of tiles) expect(t.href.startsWith(getApp(t.appId)!.path)).toBe(true)
  })

  it("keeps forge tiles under one app", () => {
    const forge = tiles.filter((t) => t.appId === "forge")
    expect(forge).toHaveLength(3)
  })

  it("reserves edit and music as coming soon", () => {
    expect(getApp("edit")?.status).toBe("coming-soon")
    expect(getApp("music")?.status).toBe("coming-soon")
  })

  it("groups every tile exactly once", () => {
    const grouped = tilesByCategory().flatMap((g) => g.tiles)
    expect(grouped).toHaveLength(tiles.length)
  })
})
