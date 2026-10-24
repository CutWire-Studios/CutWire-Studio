import { builtins, resolveTheme } from "@cutwire/drift-theme"
import { renderToString } from "react-dom/server"
import { describe, expect, it } from "vitest"
import { DriftPreview } from "../features/preview/DriftPreview"
import { PreviewProvider } from "../features/preview/kit"

function html(name: keyof typeof builtins, dialog: boolean, selected: string | null = null) {
  const doc = resolveTheme(builtins[name], builtins).document
  return renderToString(
    <PreviewProvider value={{ doc, selected, select: () => {} }}>
      <DriftPreview dialog={dialog} />
    </PreviewProvider>,
  )
}

describe("DriftPreview", () => {
  it("renders every built-in theme, with and without the dialog", () => {
    for (const name of ["dark", "light", "graphite", "frost"] as const) {
      expect(html(name, false)).toContain("data-tok=")
      expect(html(name, true)).toContain("Export project")
    }
  })

  it("paints from resolved tokens and marks the selected part", () => {
    const out = html("dark", false, "components.timelineClipItem.audioBackground")
    expect(out).toContain("rgba(143, 93, 186, 1)")
    expect(out).toMatch(/data-tok="components.timelineClipItem.audioBackground"[^>]*data-selected="true"|data-selected="true"[^>]*data-tok="components.timelineClipItem.audioBackground"/)
  })

  it("makes the window translucent for graphite", () => {
    expect(html("graphite", false)).toContain("backdrop-filter:blur(24px)")
    expect(html("dark", false)).not.toContain("backdrop-filter")
  })
})
