import { builtins, resolveTheme } from "@cutwire/drift-theme"
import { describe, expect, it } from "vitest"
import { contrastRows, rowLevel } from "../features/contrast/pairs"

describe("contrast pairs", () => {
  it("measures every pair on the built-in dark theme", () => {
    const r = resolveTheme(builtins.dark, builtins)
    const rows = contrastRows(r.document, r.variant)
    expect(rows.every((x) => x.ratio !== null)).toBe(true)
    const panel = rows.find((x) => x.id === "panel")!
    expect(panel.ratio!).toBeGreaterThan(10)
    expect(rowLevel(panel, false)).toBe("aaa")
  })

  it("flags a bad pair and relaxes it for large text", () => {
    const r = resolveTheme({ name: "bad", palette: { panelForeground: "#555555", panelBackground: "#444444" } }, builtins)
    const row = contrastRows(r.document, r.variant).find((x) => x.id === "panel")!
    expect(row.ratio!).toBeLessThan(3)
    expect(rowLevel(row, false)).toBe("fail")
  })
})
