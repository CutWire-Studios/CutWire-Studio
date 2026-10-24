import { contrastLevel, getPath, wcagContrast, type ContrastLevel, type JsonObject, type Variant } from "@cutwire/drift-theme"

export interface PairDef {
  id: string
  label: string
  fg: string
  bg: string
}

const CI = "components.timelineClipItem"
const TB = "components.themedButton"

export const PAIRS: PairDef[] = [
  { id: "app", label: "App text on app background", fg: "semantic.foreground", bg: "semantic.surface.app" },
  { id: "panel", label: "Panel text on panel background", fg: "semantic.panel.foreground", bg: "semantic.surface.panel" },
  { id: "muted", label: "Muted text on panel", fg: "semantic.mutedForeground", bg: "semantic.surface.panel" },
  { id: "primary", label: "Primary button text", fg: `${TB}.primaryForeground`, bg: `${TB}.primaryBackground` },
  { id: "button", label: "Button text", fg: `${TB}.foreground`, bg: `${TB}.background` },
  { id: "input", label: "Input text", fg: "components.themedTextField.foreground", bg: "components.themedTextField.background" },
  { id: "placeholder", label: "Input placeholder", fg: "components.themedTextField.placeholderForeground", bg: "components.themedTextField.background" },
  { id: "dialog", label: "Dialog text", fg: "components.themedDialog.foreground", bg: "components.themedDialog.background" },
  { id: "chip", label: "Chip text (primary)", fg: "components.themedChip.primaryForeground", bg: "components.themedChip.primaryBackground" },
  { id: "export", label: "Export button text", fg: "components.editorHeaderExportButton.foreground", bg: "components.editorHeaderExportButton.gradientTop" },
  { id: "clip-video", label: "Clip label on video clip", fg: `${CI}.foreground`, bg: `${CI}.videoBackground` },
  { id: "clip-audio", label: "Clip label on audio clip", fg: `${CI}.foreground`, bg: `${CI}.audioBackground` },
  { id: "clip-text", label: "Clip label on text clip", fg: `${CI}.foreground`, bg: `${CI}.textBackground` },
  { id: "clip-subtitle", label: "Clip label on subtitle clip", fg: `${CI}.foreground`, bg: `${CI}.subtitleBackground` },
  { id: "clip-graphic", label: "Clip label on graphic clip", fg: `${CI}.foreground`, bg: `${CI}.graphicBackground` },
  { id: "ruler", label: "Ruler labels", fg: "components.timelineRulerTick.foregroundMuted", bg: "components.timelinePanel.background" },
  { id: "track", label: "Track names", fg: "components.trackHeaderInlineName.foregroundMuted", bg: "components.timelinePanel.background" },
  { id: "timecode", label: "Timecode", fg: "components.previewTransportTimecode.foreground", bg: "components.previewPanel.background" },
  { id: "toast", label: "Toast text", fg: "components.toastHost.foreground", bg: "components.toastHost.background" },
]

export interface ContrastRow extends PairDef {
  ratio: number | null
  fgValue: string
  bgValue: string
}

export const THRESHOLD = 4.5
export const THRESHOLD_LARGE = 3

/** `under` is what shows through translucent fills: the wallpaper, approximated by black or white. */
export function contrastRows(resolved: JsonObject, variant: Variant): ContrastRow[] {
  const translucent = getPath(resolved, "window.translucent") === true
  const under = translucent && variant === "light" ? "#ffffff" : "#000000"
  return PAIRS.map((p) => {
    const fg = getPath(resolved, p.fg)
    const bg = getPath(resolved, p.bg)
    const fgValue = typeof fg === "string" ? fg : ""
    const bgValue = typeof bg === "string" ? bg : ""
    return { ...p, fgValue, bgValue, ratio: fgValue && bgValue ? wcagContrast(fgValue, bgValue, under) : null }
  })
}

export function rowLevel(row: ContrastRow, large: boolean): ContrastLevel | null {
  return row.ratio === null ? null : contrastLevel(row.ratio, large)
}
