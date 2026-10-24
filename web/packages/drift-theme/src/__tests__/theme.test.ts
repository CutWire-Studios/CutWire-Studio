import { strFromU8, unzipSync, zipSync, strToU8 } from "fflate"
import { describe, expect, it } from "vitest"
import {
  builtins,
  catalog,
  deepEqual,
  diffAgainstBase,
  exportJson,
  exportZip,
  findToken,
  formatColor,
  getPath,
  importTheme,
  mergeLayers,
  mergeTheme,
  parseColor,
  resolveRefs,
  resolveTheme,
  splitVariants,
  ThemeError,
  ThemeImportError,
  toCss,
  validateTheme,
  wcagContrast,
  type ThemeDoc,
} from "../index"

const lib = builtins

describe("colors", () => {
  it("reads 8-digit colors alpha first (Qt), not CSS order", () => {
    expect(parseColor("#80ff0000")).toEqual({ a: 0x80, r: 255, g: 0, b: 0 })
    expect(parseColor("#ff0000")).toEqual({ a: 255, r: 255, g: 0, b: 0 })
    expect(parseColor("#f80")).toEqual({ a: 255, r: 0xff, g: 0x88, b: 0 })
    expect(parseColor("transparent")).toEqual({ a: 0, r: 0, g: 0, b: 0 })
    expect(parseColor("#12345")).toBeNull()
    expect(parseColor("red")).toBeNull()
  })

  it("formats to Qt and CSS and round-trips", () => {
    const c = parseColor("#1affffff")!
    expect(formatColor(c)).toBe("#1affffff")
    expect(formatColor(c, "css")).toBe("rgba(255, 255, 255, 0.102)")
    expect(formatColor({ r: 1, g: 2, b: 3, a: 255 })).toBe("#010203")
    expect(toCss("#80000000")).toBe("rgba(0, 0, 0, 0.502)")
    expect(toCss("monospace")).toBe("monospace")
  })
})

describe("mergeTheme", () => {
  it("merges objects by key and replaces everything else", () => {
    expect(mergeTheme({ a: { b: 1, c: 2 }, l: [1, 2] }, { a: { c: 3 }, l: [9] })).toEqual({ a: { b: 1, c: 3 }, l: [9] })
    expect(mergeTheme({ a: { b: 1 } }, { a: "x" })).toEqual({ a: "x" })
  })
})

describe("resolveRefs", () => {
  it("resolves chains, trims, and resolves into objects and arrays", () => {
    const { value, issues } = resolveRefs({
      palette: { a: " #112233 " },
      semantic: { x: "$palette.a", y: "$semantic.x", list: ["$semantic.y", "#fff"] },
      metrics: { m: { pointer: 1, touch: 2 }, n: "$metrics.m" },
    })
    expect(issues).toEqual([])
    expect(getPath(value, "semantic.y")).toBe("#112233")
    expect(getPath(value, "semantic.list")).toEqual(["#112233", "#fff"])
    expect(getPath(value, "metrics.n")).toEqual({ pointer: 1, touch: 2 })
  })

  it("leaves cycles and unknown paths raw and reports them", () => {
    const { value, issues } = resolveRefs({
      semantic: { a: "$semantic.b", b: "$semantic.a", self: "$semantic.self", gone: "$semantic.nope", bad: "$semantic..x" },
    })
    expect(getPath(value, "semantic.a")).toBe("$semantic.b")
    expect(getPath(value, "semantic.self")).toBe("$semantic.self")
    expect(getPath(value, "semantic.gone")).toBe("$semantic.nope")
    expect(getPath(value, "semantic.bad")).toBe("$semantic..x")
    const kinds = issues.map((i) => i.kind)
    expect(kinds).toContain("cycle")
    expect(kinds).toContain("unresolved")
    expect(kinds).toContain("malformed")
    expect(issues.find((i) => i.kind === "cycle")?.path).toBeTruthy()
  })

  it("treats chains deeper than 64 as cycles", () => {
    const palette: Record<string, string> = { k0: "#fff" }
    for (let i = 1; i <= 70; i++) palette[`k${i}`] = `$palette.k${i - 1}`
    const { issues, value } = resolveRefs({ palette })
    expect(getPath(value, "palette.k10")).toBe("#fff")
    expect(issues.some((i) => i.kind === "cycle")).toBe(true)
    expect(String(getPath(value, "palette.k70")).startsWith("$")).toBe(true)
  })
})

describe("resolveTheme", () => {
  const dark = builtins.dark
  const light = builtins.light

  it("resolves real tokens through references for dark and light", () => {
    const d = resolveTheme(dark, lib).document
    const l = resolveTheme(light, lib).document
    expect(getPath(d, "components.themedButton.background")).toBe("#262626")
    expect(getPath(l, "components.themedButton.background")).toBe("#ededed")
    expect(getPath(d, "components.themedButton.primaryBackground")).toBe("#F8B81C")
    expect(getPath(d, "components.themedButton.radius")).toBe(5.6)
    expect(getPath(d, "semantic.clip.adjustment.video")).toBe(getPath(d, "palette.clipEffect"))
    expect(getPath(d, "metrics.controlHeight")).toEqual({ pointer: 30, touch: 44 })
  })

  it("resolves every built-in cleanly", () => {
    for (const name of Object.keys(builtins)) {
      const r = resolveTheme(builtins[name], lib)
      expect(r.issues, name).toEqual([])
    }
  })

  it("graphite extends dark: own keys win, the rest falls through", () => {
    const g = resolveTheme(builtins.graphite, lib)
    expect(g.variant).toBe("dark")
    expect(g.name).toBe("graphite")
    expect(getPath(g.document, "semantic.surface.app")).toBe("#161618")
    expect(getPath(g.document, "palette.clipText")).toBe(getPath(resolveTheme(dark, lib).document, "palette.clipText"))
    expect(getPath(g.document, "window.translucent")).toBe(true)
    expect(getPath(g.document, "components.panelFrame.background")).toBe("#00000000")
  })

  it("frost extends light", () => {
    const f = resolveTheme(builtins.frost, lib)
    expect(f.variant).toBe("light")
    expect(getPath(f.document, "palette.clipAudio")).toBe(getPath(resolveTheme(light, lib).document, "palette.clipAudio"))
  })

  it("fills missing tokens from the same-variant built-in", () => {
    const doc: ThemeDoc = { name: "mine", variant: "light", palette: { primary: "#ff0000" } }
    const r = resolveTheme(doc, lib)
    expect(getPath(r.document, "components.themedButton.primaryBackground")).toBe("#ff0000")
    expect(getPath(r.document, "palette.foreground")).toBe(getPath(resolveTheme(light, lib).document, "palette.foreground"))
    expect(r.schemaVersion).toBe(0)
    expect(r.document.schemaVersion).toBe(1)
  })

  it("applies the requested side when both overlays exist", () => {
    const doc: ThemeDoc = {
      name: "dual",
      variants: { dark: { palette: { primary: "#111111" } }, light: { palette: { primary: "#eeeeee" } } },
    }
    const d = mergeLayers(doc, lib, { variant: "dark" })
    const l = mergeLayers(doc, lib, { variant: "light" })
    expect(d.hasVariants).toBe(true)
    expect(getPath(d.merged, "palette.primary")).toBe("#111111")
    expect(getPath(l.merged, "palette.primary")).toBe("#eeeeee")
    expect(l.variant).toBe("light")
    expect(getPath(l.merged, "palette.foreground")).toBe(getPath(builtins.light, "palette.foreground"))
  })

  it("a lone overlay applies and decides the variant", () => {
    const r = mergeLayers({ name: "x", variants: { light: { palette: { primary: "#abcdef" } } } }, lib)
    expect(r.hasVariants).toBe(false)
    expect(r.variant).toBe("light")
    expect(getPath(r.merged, "palette.primary")).toBe("#abcdef")
  })

  it("rejects unknown and cyclic extends", () => {
    expect(() => resolveTheme({ name: "a", extends: "nope" }, lib)).toThrow(ThemeError)
    const cyc = { ...lib, a: { name: "a", extends: "b" }, b: { name: "b", extends: "a" } }
    expect(() => resolveTheme(cyc.a, cyc)).toThrow(/cycle/)
  })

  it("reports invalid colours and broken references from the user's tokens", () => {
    const r = resolveTheme({ name: "t", palette: { primary: "#12", foreground: "$palette.nope" } }, lib)
    expect(r.issues.map((i) => i.kind).sort()).toEqual(["invalid-color", "unresolved"].sort())
  })
})

describe("diffAgainstBase", () => {
  const sample: ThemeDoc = {
    name: "sample",
    schemaVersion: 1,
    extends: "graphite",
    variant: "dark",
    palette: { primary: "#ff0066", appBackground: "#161618", foreground: "#ececec", accent: "#2a2a2c" },
    semantic: { primary: "$palette.primary", surface: { panel: "#101010" } },
    components: { themedButton: { radius: "$metrics.radius.sm", border: "#00ff00" } },
    metrics: { controlHeight: { pointer: 30, touch: 50 }, radius: { sm: 5.6 } },
    window: { translucent: true, backdrop: "blur", backgroundOpacity: 0.8 },
  }

  it("keeps only real overrides", () => {
    const d = diffAgainstBase(sample, lib)
    expect(d.palette).toEqual({ primary: "#ff0066" })
    expect(d.semantic).toEqual({ surface: { panel: "#101010" } })
    expect(d.components).toEqual({ themedButton: { border: "#00ff00" } })
    expect(d.metrics).toEqual({ controlHeight: { touch: 50 } })
    expect(d.window).toBeUndefined()
    expect(d.name).toBe("sample")
    expect(d.extends).toBe("graphite")
  })

  it("round-trips: resolve(diff(x)) equals resolve(x)", () => {
    const docs: ThemeDoc[] = [
      sample,
      { ...(builtins.dark as ThemeDoc), name: "full-dark" },
      { name: "full-light", variant: "light", palette: { ...(builtins.light.palette as object), primary: "#123456" } },
      { name: "dual", extends: "dark", palette: { primary: "#f0f" }, variants: { light: { palette: { primary: "#0f0" } } } },
    ]
    for (const doc of docs) {
      const clean = JSON.parse(JSON.stringify(doc)) as ThemeDoc
      const diff = diffAgainstBase(clean, lib)
      for (const variant of ["dark", "light"] as const) {
        expect(deepEqual(resolveTheme(diff, lib, { variant }).document, resolveTheme(clean, lib, { variant }).document)).toBe(true)
      }
    }
  })

  it("shrinks a full export of dark to almost nothing", () => {
    const full = { ...JSON.parse(JSON.stringify(builtins.dark)), name: "copy" } as ThemeDoc
    const d = diffAgainstBase(full, lib)
    expect(JSON.stringify(d).length).toBeLessThan(200)
  })
})

describe("validateTheme", () => {
  it("accepts the built-ins' shape (name aside) and a minimal theme", () => {
    expect(validateTheme({ name: "ok", schemaVersion: 1, extends: "dark", variant: "dark", palette: { primary: "#ff0000" } }).valid).toBe(true)
    const full = { ...JSON.parse(JSON.stringify(builtins.dark)), name: "full" } as ThemeDoc
    expect(validateTheme(full).errors).toEqual([])
  })

  it("rejects reserved and unsafe names", () => {
    for (const name of ["dark", "light", "system", "graphite", "frost", ".hidden", "a/b", "a..b", "a\\b", "x\u0001"]) {
      expect(validateTheme({ name, schemaVersion: 1 }).errors.some((e) => e.path === "name"), name).toBe(true)
    }
    expect(validateTheme({ schemaVersion: 1 }).errors.some((e) => e.path === "name")).toBe(true)
  })

  it("enforces schemaVersion 1 and the JSON schema", () => {
    expect(validateTheme({ name: "x", schemaVersion: 2 }).errors.some((e) => e.path === "schemaVersion")).toBe(true)
    expect(validateTheme({ name: "x" }).warnings.some((e) => e.path === "schemaVersion")).toBe(true)
    const bad = validateTheme({ name: "x", schemaVersion: 1, variant: "blue", palette: { primary: "red" }, nonsense: 1, window: { backgroundOpacity: 3 } })
    const paths = bad.errors.map((e) => e.path)
    expect(paths).toContain("variant")
    expect(paths).toContain("palette.primary")
    expect(paths).toContain("")
    expect(paths).toContain("window.backgroundOpacity")
  })

  it("rejects name/extends inside variant overlays, cycles and unresolved refs", () => {
    expect(validateTheme({ name: "x", schemaVersion: 1, variants: { dark: { name: "no" } } }).valid).toBe(false)
    const r = validateTheme({ name: "x", schemaVersion: 1, palette: { primary: "$palette.primary", foreground: "$palette.missing" } })
    expect(r.errors.map((e) => e.code)).toEqual(expect.arrayContaining(["resolve.cycle", "resolve.unresolved"]))
    expect(validateTheme({ name: "x", schemaVersion: 1, extends: "ghost" }).errors.some((e) => e.code.startsWith("extends"))).toBe(true)
  })

  it("warns about 4-digit colours", () => {
    expect(validateTheme({ name: "x", schemaVersion: 1, palette: { primary: "#f00a" } }).warnings.map((w) => w.code)).toContain("color.short-argb")
  })
})

describe("wcagContrast", () => {
  it("matches the reference values", () => {
    expect(wcagContrast("#000000", "#ffffff")).toBeCloseTo(21, 5)
    expect(wcagContrast("#ffffff", "#ffffff")).toBeCloseTo(1, 5)
    expect(wcagContrast("#777777", "#ffffff")).toBeCloseTo(4.48, 1)
    expect(wcagContrast("#nope", "#ffffff")).toBeNull()
  })

  it("flattens translucent colours", () => {
    expect(wcagContrast("#ffffff", "#80000000", "#000000")).toBeGreaterThan(15)
    expect(wcagContrast("#ffffff", "#80000000", "#ffffff")).toBeLessThan(wcagContrast("#ffffff", "#80000000", "#000000")!)
    expect(wcagContrast("#00000000", "#ffffff")).toBeCloseTo(1, 5)
  })
})

describe("export and import", () => {
  const doc: ThemeDoc = {
    palette: { primary: "#ff0066" },
    variant: "dark",
    extends: "dark",
    name: "pink",
    variants: { dark: { palette: { accent: "#222222" } }, light: { palette: { accent: "#dddddd" } } },
  }

  it("orders keys and forces schemaVersion", () => {
    const text = exportJson(doc)
    expect(Object.keys(JSON.parse(text) as object)).toEqual(["name", "schemaVersion", "extends", "variant", "variants", "palette"])
    expect(JSON.parse(text).schemaVersion).toBe(1)
  })

  it("zips theme.json only, or with side files, never qml", () => {
    const plain = unzipSync(exportZip({ name: "p", palette: {} }))
    expect(Object.keys(plain)).toEqual(["theme.json"])

    const { doc: body, sides } = splitVariants(doc)
    const files = unzipSync(exportZip(body, sides))
    expect(Object.keys(files).sort()).toEqual(["theme.dark.json", "theme.json", "theme.light.json"])
    expect(Object.keys(files).some((n) => n.includes("qml"))).toBe(false)
    expect(JSON.parse(strFromU8(files["theme.json"])).variants).toBeUndefined()
    expect(JSON.parse(strFromU8(files["theme.dark.json"]))).toEqual({ palette: { accent: "#222222" } })
  })

  it("refuses side files with meta keys and oversize themes", () => {
    expect(() => exportZip({ name: "x" }, { dark: { name: "no" } })).toThrow()
    expect(() => exportJson({ name: "x", palette: { big: "x".repeat(3 * 1024 * 1024) } })).toThrow(/2 MB/)
  })

  it("imports what it exports, folding side files into variants", () => {
    const { doc: body, sides } = splitVariants(doc)
    const back = importTheme("pink.zip", exportZip(body, sides))
    expect(back.variants).toEqual(doc.variants)
    expect(back.name).toBe("pink")
    expect(importTheme("t.json", strToU8(exportJson(doc))).name).toBe("pink")
  })

  it("strips a single top-level folder and ignores junk", () => {
    const zip = zipSync({
      "pink/theme.json": strToU8('{"name":"pink"}'),
      "__MACOSX/pink/._theme.json": strToU8("x"),
      "pink/.DS_Store": strToU8("x"),
    })
    expect(importTheme("a.zip", zip).name).toBe("pink")
  })

  it("rejects qml, traversal, and too many entries", () => {
    const withQml = zipSync({ "theme.json": strToU8("{}"), "qml/ThemedButtonBackground.qml": strToU8("Item {}") })
    expect(() => importTheme("a.zip", withQml)).toThrow(ThemeImportError)
    try {
      importTheme("a.zip", withQml)
    } catch (e) {
      expect((e as ThemeImportError).code).toBe("qml")
    }
    expect(() => importTheme("a.zip", zipSync({ "theme.json": strToU8("{}"), "../evil.json": strToU8("{}") }))).toThrow(/not allowed/)
    const many: Record<string, Uint8Array> = { "theme.json": strToU8("{}") }
    for (let i = 0; i < 70; i++) many[`f${i}.txt`] = strToU8("x")
    expect(() => importTheme("a.zip", zipSync(many))).toThrow(/too many/)
    expect(() => importTheme("a.zip", zipSync({ "readme.txt": strToU8("x") }))).toThrow(/does not contain a theme/)
    expect(() => importTheme("a.json", strToU8("[1]"))).toThrow(/object/)
  })

  it("accepts a lone json in a zip as the theme", () => {
    expect(importTheme("a.zip", zipSync({ "mine.json": strToU8('{"name":"mine"}') })).name).toBe("mine")
  })
})

describe("catalog", () => {
  it("covers every category and the whole built-in dark document", () => {
    expect(catalog.categories).toHaveLength(7)
    expect(catalog.tokens.filter((t) => t.category === "components")).toHaveLength(2426)
    expect(new Set(catalog.tokens.filter((t) => t.category === "components").map((t) => t.group)).size).toBeGreaterThan(500)
    const t = findToken("components.themedButton.background")!
    expect(t).toMatchObject({ type: "color", group: "themedButton", dark: "$semantic.panel.accent" })
    expect(findToken("metrics.controlHeight")?.type).toBe("pair")
    expect(findToken("motion.easing")).toMatchObject({ type: "enum" })
    expect(findToken("window.translucent")?.type).toBe("boolean")
    expect(findToken("typography.monoFontFamily")?.type).toBe("font")
    expect(findToken("semantic.category")?.type).toBe("array")
  })
})
