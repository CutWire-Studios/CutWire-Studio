import { Bell, Download, FolderOpen, Image as ImageIcon, Music, Pause, Play, Search, SkipBack, SkipForward, Sparkles, Type as TypeIcon, Volume2, Maximize, Layers } from "lucide-react"
import type { CSSProperties, ReactNode } from "react"
import { IconBtn } from "./controls"
import { flex, Part, useTok, type Tok } from "./kit"

export function panelStyle(t: Tok, group: string): CSSProperties {
  return {
    background: t.c(`components.${group}.background`),
    border: `${t.n(`components.${group}.borderWidth`, 1)}px solid ${t.c(`components.${group}.border`)}`,
    borderRadius: t.n(`components.${group}.radius`, 5),
    boxSizing: "border-box",
    overflow: "hidden",
    minWidth: 0,
    minHeight: 0,
  }
}

export function Header() {
  const t = useTok()
  return (
    <Part
      sel="components.editorHeader.background"
      style={{
        ...flex({ justifyContent: "space-between", padding: `0 ${t.n("metrics.pagePadding", 12)}px` }),
        height: t.n("metrics.headerHeight", 54),
        flex: "none",
        background: t.c("components.editorHeader.background"),
        color: t.c("semantic.foreground"),
      }}
    >
      <div style={flex({ gap: 10 })}>
        <Part sel="semantic.primary" style={{ ...flex({ justifyContent: "center" }), width: 26, height: 26, borderRadius: 8, background: t.c("semantic.primary"), color: t.c("semantic.primaryForeground"), fontWeight: 700 }}>
          D
        </Part>
        <span style={{ fontWeight: t.n("typography.fontWeight.semibold", 600), fontSize: t.n("typography.fontSize.base", 14.7) }}>Summer trip</span>
        <Part sel="semantic.mutedForeground" style={{ color: t.c("semantic.mutedForeground"), fontSize: t.n("typography.fontSize.sm", 12.6) }}>
          Saved
        </Part>
      </div>
      <div style={flex({ gap: 4 })}>
        <IconBtn icon={FolderOpen} />
        <IconBtn icon={Bell} active />
        <IconBtn icon={Search} />
        <Part
          sel="components.editorHeaderExportButton.gradientTop"
          style={{
            ...flex({ gap: 6, padding: "0 14px", marginLeft: 8, justifyContent: "center" }),
            height: t.n("metrics.controlHeight", 30),
            borderRadius: t.n("components.editorHeaderExportButton.radius", 5),
            background: `linear-gradient(${t.c("components.editorHeaderExportButton.gradientTop")}, ${t.c("components.editorHeaderExportButton.gradientBottom")})`,
            boxShadow: `0 0 14px ${t.c("components.editorHeaderExportButton.glow")}55`,
            color: t.c("components.editorHeaderExportButton.foreground"),
            fontWeight: 600,
            fontSize: t.n("typography.fontSize.sm", 12.6),
          }}
        >
          <Download size={14} color={t.c("components.editorHeaderExportButton.icon")} />
          Export
        </Part>
      </div>
    </Part>
  )
}

const ASSETS: Array<{ name: string; dur: string; hue: number }> = [
  { name: "beach.mp4", dur: "0:12", hue: 200 },
  { name: "drone.mp4", dur: "0:34", hue: 140 },
  { name: "sunset.mp4", dur: "0:08", hue: 20 },
  { name: "city.mp4", dur: "1:02", hue: 260 },
  { name: "waves.wav", dur: "2:10", hue: 0 },
  { name: "logo.png", dur: "", hue: 330 },
]

export function AssetsPanel() {
  const t = useTok()
  const tabs: Array<[typeof ImageIcon, boolean]> = [[ImageIcon, true], [Music, false], [TypeIcon, false], [Sparkles, false], [Layers, false]]
  return (
    <Part sel="components.assetsPanel.background" style={{ ...panelStyle(t, "assetsPanel"), width: 300, flex: "none", display: "flex", flexDirection: "column" }}>
      <Part
        sel="components.assetsPanelHeader.background"
        style={{ ...flex({ gap: 2, padding: "0 8px" }), height: t.n("metrics.panelHeaderHeight", 44), flex: "none", background: t.c("components.assetsPanelHeader.background"), borderBottom: `${t.n("components.assetsPanelHeader.borderWidth", 1)}px solid ${t.c("components.assetsPanelHeader.border")}` }}
      >
        {tabs.map(([Icon, on], i) => (
          <Part
            key={i}
            sel={on ? "components.navRailButton.backgroundSelected" : "components.navRailButton.foreground"}
            style={{ ...flex({ justifyContent: "center" }), width: 32, height: 28, borderRadius: t.n("components.navRailButton.radius", 5), background: on ? t.c("components.navRailButton.backgroundSelected") : "transparent", color: t.c(on ? "components.navRailButton.foreground" : "components.navRailButton.foregroundMuted") }}
          >
            <Icon size={16} />
          </Part>
        ))}
        <Part sel="components.assetsPanelHeader.foregroundMuted" style={{ marginLeft: "auto", fontSize: t.n("typography.fontSize.xs", 11.5), color: t.c("components.assetsPanelHeader.foregroundMuted") }}>
          6 items
        </Part>
      </Part>
      <div style={{ display: "grid", gridTemplateColumns: "repeat(2, 1fr)", gap: t.n("metrics.assetCardGap", 16) - 6, padding: 12 }}>
        {ASSETS.map((a, i) => (
          <div key={a.name} style={{ position: "relative", minWidth: 0 }}>
            <Part
              sel="components.mediaAssetsGridFrame.background"
              style={{
                position: "relative",
                height: 72,
                boxSizing: "border-box",
                borderRadius: t.n("components.mediaAssetsGridFrame.radius", 5),
                background: `linear-gradient(135deg, hsl(${a.hue} 40% 30% / .9), ${t.c("components.mediaAssetsGridFrame.background")})`,
                border: `${t.n("components.mediaAssetsGridFrame.borderWidth", 1)}px solid ${i === 1 ? t.c("components.mediaAssetsGridFrame.border") : "transparent"}`,
                overflow: "hidden",
              }}
            >
              <span style={{ position: "absolute", left: 6, top: 6, color: t.c("components.mediaAssetsGridFrame.icon") }}>
                {a.hue === 0 ? <Volume2 size={14} /> : <Play size={14} />}
              </span>
              {a.dur && (
                <Part
                  sel="components.mediaAssetsDurationBadge.background"
                  style={{ position: "absolute", right: 4, bottom: 4, padding: "1px 5px", borderRadius: t.n("components.mediaAssetsDurationBadge.radius", 4), background: t.c("components.mediaAssetsDurationBadge.background"), color: t.c("components.mediaAssetsDurationBadge.foreground"), fontSize: t.n("typography.fontSize.tiny", 9.6) }}
                >
                  {a.dur}
                </Part>
              )}
            </Part>
            {i === 1 && (
              <Part
                sel="components.mediaAssetsSelectionRing.border"
                style={{ position: "absolute", inset: -2, borderRadius: t.n("components.mediaAssetsGridFrame.radius", 5) + 2, border: `${t.n("components.mediaAssetsSelectionRing.borderWidth", 1.5)}px solid ${t.c("components.mediaAssetsSelectionRing.border")}`, pointerEvents: "none" }}
              />
            )}
            <Part sel="components.mediaAssetsGridNameRow.foregroundMuted" style={{ marginTop: 4, fontSize: t.n("typography.fontSize.card", 11.2), color: t.c("components.mediaAssetsGridNameRow.foregroundMuted"), whiteSpace: "nowrap", overflow: "hidden", textOverflow: "ellipsis" }}>
              {a.name}
            </Part>
          </div>
        ))}
      </div>
    </Part>
  )
}

function Transport({ children }: { children: ReactNode }) {
  const t = useTok()
  return (
    <Part
      sel="components.previewTransportBar.background"
      style={{ ...flex({ gap: 6, padding: `0 ${t.n("metrics.previewTransportPadding", 6) + 6}px` }), height: 40, flex: "none", background: t.c("components.previewTransportBar.background") }}
    >
      {children}
    </Part>
  )
}

export function PreviewPanel() {
  const t = useTok()
  return (
    <Part sel="components.previewPanel.background" style={{ ...panelStyle(t, "previewPanel"), flex: 1, display: "flex", flexDirection: "column", borderRadius: t.n("components.previewPanel.radius", 5) }}>
      <Part sel="components.previewViewport.background" style={{ flex: 1, minHeight: 0, position: "relative", ...flex({ justifyContent: "center" }), background: t.c("components.previewViewport.background"), border: `${t.n("components.previewViewport.borderWidth", 1)}px solid ${t.c("components.previewViewport.border")}` }}>
        <Part sel="semantic.preview.letterbox" style={{ position: "absolute", inset: 0, background: t.c("components.previewViewport.letterbox") }} />
        <div style={{ position: "relative", height: "92%", aspectRatio: "16 / 9", background: "linear-gradient(160deg, #1f4e79 0%, #e08a3c 60%, #2b1b3d 100%)", boxShadow: "0 4px 24px #0008" }}>
          <Part
            sel="components.previewOsdCluster.background"
            style={{ ...flex({ gap: 6, padding: "2px 8px" }), position: "absolute", left: 8, top: 8, background: t.c("components.previewOsdCluster.background"), border: `${t.n("components.previewOsdCluster.borderWidth", 1)}px solid ${t.c("components.previewOsdCluster.border")}`, borderRadius: t.n("components.previewOsdCluster.radius", 6), fontSize: t.n("typography.fontSize.xs", 11.5), color: t.c("components.previewOsdZoomChip.foreground") }}
          >
            100%
          </Part>
        </div>
      </Part>
      <Transport>
        <IconBtn icon={SkipBack} size={26} />
        <IconBtn icon={Pause} active size={26} />
        <IconBtn icon={SkipForward} size={26} />
        <Part sel="components.previewTransportTimecode.foreground" style={{ fontFamily: t.s("typography.monoFontFamily", "monospace"), fontSize: t.n("typography.fontSize.sm", 12.6), color: t.c("components.previewTransportTimecode.foreground"), marginLeft: 6 }}>
          00:00:07:12
          <span style={{ color: t.c("components.previewTransportTimecode.foregroundMuted") }}> / 00:00:16:00</span>
        </Part>
        <div style={{ flex: 1 }} />
        <IconBtn icon={Volume2} size={26} />
        <IconBtn icon={Maximize} size={26} />
      </Transport>
    </Part>
  )
}
