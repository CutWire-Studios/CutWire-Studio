import { Eye, Lock, Magnet, Minus, Plus, Scissors, Undo2, Volume2, VolumeX } from "lucide-react"
import type { CSSProperties } from "react"
import { IconBtn } from "./controls"
import { flex, Part, useTok, type Tok } from "./kit"
import { panelStyle } from "./panels"

const CI = "components.timelineClipItem"
const PPS = 50
const LABELS = 130

interface Clip {
  at: number
  len: number
  name: string
  kind: "video" | "audio" | "text"
  selected?: boolean
}

const TRACKS: Array<{ name: string; key: "text" | "video" | "audio"; height: string; clips: Clip[] }> = [
  { name: "Text", key: "text", height: "trackHeightText", clips: [{ at: 1.5, len: 4, name: "Title", kind: "text" }, { at: 9, len: 3.5, name: "Credits", kind: "text" }] },
  { name: "Video 1", key: "video", height: "trackHeightVideo", clips: [{ at: 0, len: 5, name: "beach.mp4", kind: "video" }, { at: 5, len: 4.5, name: "drone.mp4", kind: "video", selected: true }, { at: 10, len: 6, name: "sunset.mp4", kind: "video" }] },
  { name: "Audio 1", key: "audio", height: "trackHeightAudio", clips: [{ at: 0, len: 15, name: "waves.wav", kind: "audio" }] },
]

function ClipView({ clip, h, t }: { clip: Clip; h: number; t: Tok }) {
  const bg = `${CI}.${clip.kind}Background`
  const style: CSSProperties = {
    position: "absolute",
    left: clip.at * PPS,
    width: clip.len * PPS - 2,
    top: 2,
    height: h - 4,
    boxSizing: "border-box",
    overflow: "hidden",
    borderRadius: t.n(`${CI}.radius`, 5),
    background: t.c(bg),
    color: t.c(`${CI}.foreground`),
    border: clip.selected ? `${t.n(`${CI}.selectionBorderWidth`, 1.5)}px solid ${t.c(`${CI}.accent`)}` : `1px solid ${t.c(`${CI}.border`)}`,
    fontSize: t.n("typography.fontSize.xs", 11.5),
  }
  const bars = Array.from({ length: Math.floor((clip.len * PPS) / 4) }, (_, i) => 4 + Math.abs(Math.sin(i * 0.7) * Math.cos(i * 0.21)) * (h - 18))
  return (
    <Part sel={bg} style={style}>
      <div style={{ padding: "2px 6px", height: t.n("metrics.timeline.clipHeaderBandHeight", 20), boxSizing: "border-box", whiteSpace: "nowrap", fontWeight: 500 }}>{clip.name}</div>
      {clip.kind === "audio" && (
        <Part sel={`${CI}.waveform`} style={{ ...flex({ gap: 1, justifyContent: "center" }), position: "absolute", inset: "18px 0 0 0" }}>
          {bars.map((b, i) => (
            <span key={i} style={{ width: 3, height: b, flex: "none", borderRadius: 1, background: t.c(`${CI}.waveform`) }} />
          ))}
        </Part>
      )}
      {clip.kind === "video" && clip.selected && (
        <>
          <Part sel="components.timelineTrimHandle.background" style={{ position: "absolute", left: 0, top: 0, bottom: 0, width: 6, background: t.c("components.timelineTrimHandle.background"), borderRadius: t.n("components.timelineTrimHandle.radius", 3) }} />
          <Part sel="components.timelineTrimHandle.background" style={{ position: "absolute", right: 0, top: 0, bottom: 0, width: 6, background: t.c("components.timelineTrimHandle.background"), borderRadius: t.n("components.timelineTrimHandle.radius", 3) }} />
        </>
      )}
    </Part>
  )
}

export function TimelinePanel() {
  const t = useTok()
  const gap = t.n("metrics.timeline.trackGap", 6)
  const playheadX = 7.5 * PPS
  const ticks = Array.from({ length: 21 }, (_, i) => i)
  return (
    <Part sel="components.timelinePanel.background" style={{ ...panelStyle(t, "panelFrame"), background: t.c("components.timelinePanel.background"), height: 270, flex: "none", display: "flex", flexDirection: "column" }}>
      <Part
        sel="components.timelineToolbar.background"
        style={{ ...flex({ gap: 4, padding: "0 8px" }), height: t.n("metrics.timeline.toolbarHeight", 40), flex: "none", background: t.c("components.timelineToolbar.background"), borderBottom: `1px solid ${t.c("components.timelineToolbar.border")}` }}
      >
        <IconBtn icon={Undo2} size={26} />
        <IconBtn icon={Scissors} size={26} />
        <IconBtn icon={Magnet} active size={26} />
        <Part sel="components.timelineToolbarSeparator.foreground" style={{ width: t.n("components.timelineToolbarSeparator.borderWidth", 1), height: 18, margin: "0 6px", background: t.c("components.timelineToolbarSeparator.foreground") }} />
        <Part sel="components.timelineToolbarTimecode.foregroundMuted" style={{ fontFamily: t.s("typography.monoFontFamily", "monospace"), fontSize: t.n("typography.fontSize.sm", 12.6), color: t.c("components.timelineToolbarTimecode.foregroundMuted") }}>
          00:00:07:12
        </Part>
        <div style={{ flex: 1 }} />
        <IconBtn icon={Minus} size={26} />
        <Part sel="components.timelineToolbarZoomReadout.foregroundMuted" style={{ fontSize: t.n("typography.fontSize.xs", 11.5), color: t.c("components.timelineToolbarZoomReadout.foregroundMuted") }}>
          50 px/s
        </Part>
        <IconBtn icon={Plus} size={26} />
      </Part>
      <div style={{ position: "relative", flex: 1, minHeight: 0, display: "flex", flexDirection: "column" }}>
        <div style={{ ...flex(), height: t.n("metrics.timeline.rulerHeight", 28), flex: "none", borderBottom: `1px solid ${t.c("components.timelinePanel.headerBorder")}` }}>
          <div style={{ width: LABELS, flex: "none" }} />
          <div style={{ position: "relative", flex: 1, height: "100%" }}>
            {ticks.map((i) => (
              <Part key={i} sel="components.timelineRulerTick.tick" style={{ position: "absolute", left: i * PPS, bottom: 0, height: i % 5 === 0 ? 12 : 7, width: 1, background: t.c("components.timelineRulerTick.tick") }}>
                {i % 5 === 0 && <span style={{ position: "absolute", left: 4, bottom: 8, fontSize: t.n("typography.fontSize.tick", 10), color: t.c("components.timelineRulerTick.foregroundMuted") }}>{`0:${String(i).padStart(2, "0")}`}</span>}
              </Part>
            ))}
          </div>
        </div>
        <div style={{ display: "flex", flexDirection: "column", gap, padding: `${gap}px 0`, overflow: "hidden" }}>
          {TRACKS.map((tr) => {
            const h = t.n(`metrics.timeline.${tr.height}`, 40)
            return (
              <div key={tr.key} style={flex({ height: h })}>
                <Part sel="components.trackHeaderRow.background" style={{ ...flex({ gap: 4, padding: "0 8px" }), width: LABELS, height: "100%", boxSizing: "border-box", flex: "none", background: t.c("components.trackHeaderRow.background"), borderRight: `1px solid ${t.c("components.trackHeaderEdge.border")}` }}>
                  <Part sel="components.trackHeaderInlineName.foregroundMuted" style={{ flex: 1, fontSize: t.n("typography.fontSize.xs", 11.5), color: t.c("components.trackHeaderInlineName.foregroundMuted") }}>
                    {tr.name}
                  </Part>
                  <Eye size={12} color={t.c("components.trackHeaderMuteButton.foregroundMuted")} />
                  {tr.key === "audio" ? <VolumeX size={12} color={t.c("components.trackHeaderMuteButton.destructive")} /> : <Volume2 size={12} color={t.c("components.trackHeaderMuteButton.foregroundMuted")} />}
                  <Lock size={12} color={t.c("components.trackHeaderMuteButton.foregroundMuted")} />
                </Part>
                <Part sel="components.timelinePanel.trackBackground" style={{ position: "relative", flex: 1, height: "100%", background: t.c("components.timelinePanel.trackBackground"), overflow: "hidden" }}>
                  {tr.clips.map((c) => (
                    <ClipView key={c.name} clip={c} h={h} t={t} />
                  ))}
                </Part>
              </div>
            )
          })}
        </div>
        <div style={{ position: "absolute", left: LABELS + playheadX, top: 0, bottom: 0, width: 0, pointerEvents: "none" }}>
          <Part sel="components.timelinePlayheadLine.foreground" style={{ position: "absolute", left: -1, top: 0, bottom: 0, width: t.n("metrics.timeline.playheadLineWidth", 2), background: t.c("components.timelinePlayheadLine.foreground"), pointerEvents: "auto" }} />
          <Part sel="components.timelinePlayheadHead.background" style={{ position: "absolute", left: -8, top: 0, width: 16, height: 14, background: t.c("components.timelinePlayheadHead.background"), borderRadius: `${t.n("components.timelinePlayheadHead.radius", 2)}px ${t.n("components.timelinePlayheadHead.radius", 2)}px 8px 8px`, pointerEvents: "auto" }} />
        </div>
      </div>
    </Part>
  )
}
