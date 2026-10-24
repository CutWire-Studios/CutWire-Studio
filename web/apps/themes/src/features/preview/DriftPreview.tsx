import { useTok } from "./kit"
import { Dialog, Toast } from "./overlays"
import { AssetsPanel, Header, PreviewPanel } from "./panels"
import { PropertiesPanel } from "./properties"
import { TimelinePanel } from "./timeline"

export const PREVIEW_W = 1280
export const PREVIEW_H = 720

const BACKDROPS = new Set(["blur", "acrylic", "mica", "vibrancy"])

/** A fixed 1280x720 mock of the Drift editor window, painted from the resolved theme. */
export function DriftPreview({ dialog, capture }: { dialog: boolean; capture?: boolean }) {
  const t = useTok()
  const translucent = t.b("window.translucent")
  const opacity = t.n("window.backgroundOpacity", 1)
  const backdrop = t.s("window.backdrop", "none")
  const pad = t.n("metrics.pagePadding", 12)
  const gap = t.n("metrics.panelGap", 6)
  return (
    <div
      className={capture ? "pv pv-capture" : "pv"}
      style={{
        position: "relative",
        width: PREVIEW_W,
        height: PREVIEW_H,
        overflow: "hidden",
        fontFamily: "system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif",
        fontSize: t.n("typography.fontSize.base", 14.7),
        background: "linear-gradient(120deg, #3b5bdb 0%, #9c36b5 45%, #f08c00 100%)",
      }}
    >
      <div
        data-tok="components.appWindow.background"
        data-selected={t.selected === "components.appWindow.background"}
        onClick={() => t.select("components.appWindow.background")}
        style={{
          position: "absolute",
          inset: 0,
          display: "flex",
          flexDirection: "column",
          background: translucent ? withOpacity(t.c("components.appWindow.background"), opacity) : t.c("components.appWindow.background"),
          backdropFilter: translucent && BACKDROPS.has(backdrop) ? "blur(24px) saturate(1.2)" : undefined,
          color: t.c("semantic.foreground"),
        }}
      >
        <Header />
        <div style={{ flex: 1, minHeight: 0, display: "flex", flexDirection: "column", gap, padding: `0 ${pad}px ${pad}px` }}>
          <div style={{ flex: 1, minHeight: 0, display: "flex", gap }}>
            <AssetsPanel />
            <PreviewPanel />
            <PropertiesPanel />
          </div>
          <TimelinePanel />
        </div>
        <Toast />
        {dialog && <Dialog />}
      </div>
    </div>
  )
}

/** Scales an `rgba()` colour's alpha by `k`, as WindowBackdrop does for the window fill. */
function withOpacity(css: string, k: number): string {
  const m = /^rgba\((\d+), (\d+), (\d+), ([\d.]+)\)$/.exec(css)
  if (!m) return css
  return `rgba(${m[1]}, ${m[2]}, ${m[3]}, ${Math.round(+m[4] * k * 1000) / 1000})`
}
