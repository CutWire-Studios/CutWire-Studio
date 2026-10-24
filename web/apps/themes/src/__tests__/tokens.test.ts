import { builtins, getPath, resolveTheme } from "@cutwire/drift-theme"
import { describe, expect, it } from "vitest"
import { compatibleTargets, matchesQuery, modifiedPaths, referrersOf, tokenPathFor, wouldCycle } from "../lib/tokens"
import { findToken } from "@cutwire/drift-theme"

const merged = resolveTheme(builtins.dark, builtins).merged

describe("token helpers", () => {
  it("maps leaf paths to catalog tokens", () => {
    expect(tokenPathFor("metrics.controlHeight.touch")).toBe("metrics.controlHeight")
    expect(tokenPathFor("palette.primary")).toBe("palette.primary")
    expect(tokenPathFor("palette.doesNotExist")).toBeNull()
  })

  it("finds modified tokens in the body and the current overlay", () => {
    const doc = {
      palette: { primary: "#fff" },
      metrics: { controlHeight: { touch: 50 } },
      variants: { dark: { palette: { accent: "#000" } }, light: { palette: { border: "#111" } } },
    }
    expect([...modifiedPaths(doc, "dark")].sort()).toEqual(["metrics.controlHeight", "palette.accent", "palette.primary"])
    expect(modifiedPaths(doc, "light").has("palette.border")).toBe(true)
  })

  it("lists what references a token", () => {
    const refs = referrersOf(merged, "semantic.primary")
    expect(refs).toContain("semantic.accentOnPanel")
    expect(refs).toContain("components.themedButton.primaryBackground")
  })

  it("detects reference cycles before they are created", () => {
    expect(wouldCycle(merged, "semantic.primary", "semantic.accentOnPanel")).toBe(true)
    expect(wouldCycle(merged, "semantic.primary", "palette.primary")).toBe(false)
    expect(wouldCycle(merged, "palette.primary", "palette.primary")).toBe(true)
  })

  it("offers only same-type targets from shared tiers", () => {
    const t = findToken("components.themedButton.background")!
    const targets = compatibleTargets(t)
    expect(targets.every((x) => x.type === "color" && x.category !== "components")).toBe(true)
    expect(targets.some((x) => x.path === "semantic.panel.accent")).toBe(true)
    const n = compatibleTargets(findToken("components.themedButton.radius")!)
    expect(n.some((x) => x.path === "metrics.radius.sm")).toBe(true)
    expect(getPath(merged, "metrics.radius.sm")).toBe(5.6)
  })

  it("searches by every word", () => {
    const t = findToken("components.themedButton.backgroundHover")!
    expect(matchesQuery(t, "button hover")).toBe(true)
    expect(matchesQuery(t, "slider hover")).toBe(false)
  })
})
