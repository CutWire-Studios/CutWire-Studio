import { Btn, CheckBox, Chip, Combo, Field, SectionHeader, Slider, Switch } from "./controls"
import { flex, Part, useTok } from "./kit"
import { panelStyle } from "./panels"

function Row({ label, children }: { label: string; children: React.ReactNode }) {
  const t = useTok()
  return (
    <div style={flex({ gap: 8 })}>
      <Part sel="components.themedLabel.foregroundMuted" style={{ width: 62, flex: "none", fontSize: t.n("typography.fontSize.xs", 11.5), color: t.c("components.themedLabel.foregroundMuted") }}>
        {label}
      </Part>
      {children}
    </div>
  )
}

export function PropertiesPanel() {
  const t = useTok()
  return (
    <Part sel="components.propertiesPanel.background" style={{ ...panelStyle(t, "propertiesPanel"), width: 300, flex: "none", padding: 12, display: "flex", flexDirection: "column", gap: 8, color: t.c("components.themedLabel.foreground"), fontSize: t.n("typography.fontSize.sm", 12.6) }}>
      <Part sel="components.propertiesInspectorHeading.foregroundMuted" style={{ fontSize: t.n("typography.fontSize.xs", 11.5), textTransform: "uppercase", letterSpacing: 0.6, color: t.c("components.propertiesInspectorHeading.foregroundMuted") }}>
        Inspector · beach.mp4
      </Part>
      <SectionHeader>Transform</SectionHeader>
      <Slider value={0.62} label="Scale" />
      <Slider value={0.3} label="Opacity" />
      <Row label="Position">
        <Field value="960" number />
        <Field value="540" number focus />
      </Row>
      <SectionHeader>Clip</SectionHeader>
      <Row label="Name">
        <Field value="Beach sunrise" />
      </Row>
      <Row label="Blend">
        <Combo value="Screen" />
      </Row>
      <Row label="Title">
        <Field placeholder="Add a title" />
        <Field value="Bad value" error />
      </Row>
      <div style={flex({ gap: 14 })}>
        <div style={flex({ gap: 8 })}>
          <Switch on />
          <span>Stabilise</span>
        </div>
        <div style={flex({ gap: 8 })}>
          <Switch on={false} />
          <span>Loop</span>
        </div>
      </div>
      <div style={flex({ gap: 14 })}>
        <CheckBox on label="Snap" />
        <CheckBox on={false} label="Ripple" />
      </div>
      <div style={flex({ gap: 6, flexWrap: "wrap" })}>
        <Chip label="Video" kind="primary" />
        <Chip label="4K" kind="secondary" />
        <Chip label="HDR" kind="outline" />
      </div>
      <div style={{ ...flex({ gap: 6, flexWrap: "wrap" }), marginTop: "auto" }}>
        <Btn label="Default" />
        <Btn label="Hover" kind="hover" />
        <Btn label="Pressed" kind="pressed" />
        <Btn label="Disabled" kind="disabled" />
        <Btn label="Focus" kind="focus" />
        <Btn label="Primary" kind="primary" />
        <Btn label="Delete" kind="destructive" />
      </div>
    </Part>
  )
}
