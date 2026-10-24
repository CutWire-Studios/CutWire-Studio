import type { LucideIcon } from "lucide-react"
import { Check, ChevronDown } from "lucide-react"
import type { CSSProperties, ReactNode } from "react"
import { flex, Part, useTok } from "./kit"

const TB = "components.themedButton"

export type ButtonKind = "default" | "hover" | "pressed" | "disabled" | "primary" | "destructive" | "focus"

export function Btn({ label, kind = "default", icon: Icon }: { label: string; kind?: ButtonKind; icon?: LucideIcon }) {
  const t = useTok()
  const bgKey = { default: "background", hover: "backgroundHover", pressed: "backgroundPressed", disabled: "backgroundDisabled", primary: "primaryBackground", destructive: "background", focus: "background" }[kind]
  const fg = { primary: "primaryForeground", destructive: "destructiveForeground" }[kind as "primary" | "destructive"] ?? "foreground"
  const sel = `${TB}.${{ primary: "primaryBackground", destructive: "destructiveForeground" }[kind as "primary" | "destructive"] ?? bgKey}`
  const focus = kind === "focus"
  const style: CSSProperties = {
    ...flex({ justifyContent: "center", gap: 6 }),
    height: t.n("metrics.controlHeightSm", 26),
    padding: "0 12px",
    background: t.c(`${TB}.${bgKey}`),
    color: t.c(`${TB}.${fg}`),
    border: `${focus ? t.n(`${TB}.focusBorderWidth`, 2) : t.n(`${TB}.borderWidth`, 1)}px solid ${t.c(focus ? `${TB}.focusBorder` : `${TB}.border`)}`,
    borderRadius: t.n(`${TB}.radius`, 5),
    fontSize: t.n("typography.fontSize.sm", 12.6),
    fontWeight: t.n("typography.fontWeight.medium", 500),
    opacity: kind === "disabled" ? 0.5 : 1,
    whiteSpace: "nowrap",
  }
  return (
    <Part sel={sel} style={style} title={kind}>
      {Icon && <Icon size={t.n("metrics.iconSize.base", 16) - 2} />}
      {label}
    </Part>
  )
}

export function IconBtn({ icon: Icon, active, size }: { icon: LucideIcon; active?: boolean; size?: number }) {
  const t = useTok()
  const s = size ?? t.n("metrics.iconButtonSize", 28)
  return (
    <Part
      sel={active ? "components.iconButton.foregroundActive" : "components.iconButton.foreground"}
      style={{
        ...flex({ justifyContent: "center" }),
        width: s,
        height: s,
        flex: "none",
        borderRadius: t.n("components.iconButton.radius", 5),
        background: active ? t.c("components.iconButton.backgroundActive") : "transparent",
        color: t.c(active ? "components.iconButton.foregroundActive" : "components.iconButton.foreground"),
      }}
    >
      <Icon size={t.n("metrics.iconSize.base", 16)} />
    </Part>
  )
}

export function Slider({ value, label }: { value: number; label?: string }) {
  const t = useTok()
  const h = 14
  return (
    <div style={flex({ gap: 8, flex: 1, minWidth: 0 })}>
      {label && <span style={{ width: 62, color: t.c("components.themedLabel.foregroundMuted"), fontSize: t.n("typography.fontSize.xs", 11.5) }}>{label}</span>}
      <div style={{ position: "relative", flex: 1, height: h }}>
        <Part
          sel="components.themedSlider.trackBackground"
          style={{ position: "absolute", left: 0, right: 0, top: h / 2 - 2, height: 4, borderRadius: t.n("components.themedSlider.trackRadius", 999), background: t.c("components.themedSlider.trackBackground") }}
        />
        <Part
          sel="components.themedSlider.fillBackground"
          style={{ position: "absolute", left: 0, width: `${value * 100}%`, top: h / 2 - 2, height: 4, borderRadius: t.n("components.themedSlider.trackRadius", 999), background: t.c("components.themedSlider.fillBackground") }}
        />
        <Part
          sel="components.themedSliderHandle.background"
          style={{
            position: "absolute",
            left: `calc(${value * 100}% - ${h / 2}px)`,
            top: 0,
            width: h,
            height: h,
            boxSizing: "border-box",
            borderRadius: t.n("components.themedSliderHandle.radius", 999),
            background: t.c("components.themedSliderHandle.background"),
            border: `${t.n("components.themedSliderHandle.borderWidth", 2)}px solid ${t.c("components.themedSliderHandle.border")}`,
          }}
        />
      </div>
    </div>
  )
}

export function Switch({ on }: { on: boolean }) {
  const t = useTok()
  const k = on ? "trackBackgroundChecked" : "trackBackground"
  return (
    <Part
      sel={`components.themedSwitch.${k}`}
      style={{
        position: "relative",
        width: 32,
        height: 18,
        flex: "none",
        boxSizing: "border-box",
        borderRadius: t.n("components.themedSwitch.trackRadius", 999),
        background: t.c(`components.themedSwitch.${k}`),
        border: `${t.n("components.themedSwitch.trackBorderWidth", 1)}px solid ${t.c(on ? "components.themedSwitch.trackBorderChecked" : "components.themedSwitch.trackBorder")}`,
      }}
    >
      <Part
        sel="components.themedSwitch.handleBackground"
        style={{ position: "absolute", top: 2, left: on ? 15 : 2, width: 12, height: 12, borderRadius: t.n("components.themedSwitch.handleRadius", 999), background: t.c("components.themedSwitch.handleBackground") }}
      />
    </Part>
  )
}

export function CheckBox({ on, label }: { on: boolean; label: string }) {
  const t = useTok()
  return (
    <div style={flex({ gap: 8 })}>
      <Part
        sel={on ? "components.themedCheckBox.backgroundChecked" : "components.themedCheckBox.background"}
        style={{
          ...flex({ justifyContent: "center" }),
          width: 16,
          height: 16,
          flex: "none",
          boxSizing: "border-box",
          borderRadius: t.n("components.themedCheckBox.radius", 3),
          background: t.c(on ? "components.themedCheckBox.backgroundChecked" : "components.themedCheckBox.background"),
          border: `${t.n("components.themedCheckBox.borderWidth", 1)}px solid ${t.c(on ? "components.themedCheckBox.borderChecked" : "components.themedCheckBox.border")}`,
          color: t.c("components.themedCheckBox.icon"),
        }}
      >
        {on && <Check size={12} strokeWidth={3} />}
      </Part>
      <Part sel="components.themedCheckBox.foreground" style={{ color: t.c("components.themedCheckBox.foreground"), fontSize: t.n("typography.fontSize.sm", 12.6) }}>
        {label}
      </Part>
    </div>
  )
}

export function Field({ value, placeholder, focus, error, number }: { value?: string; placeholder?: string; focus?: boolean; error?: boolean; number?: boolean }) {
  const t = useTok()
  const g = number ? "themedNumberField" : "themedTextField"
  const border = error ? "borderError" : { true: "borderFocus", false: "border" }[String(!!focus) as "true" | "false"]
  return (
    <Part
      sel={`components.${g}.background`}
      style={{
        ...flex({ minWidth: 0, flex: 1 }),
        height: t.n("metrics.controlHeightSm", 26),
        boxSizing: "border-box",
        padding: "0 8px",
        background: t.c(`components.${g}.background`),
        border: `${t.n(`components.${g}.${focus || error ? "borderWidthFocus" : "borderWidth"}`, 1)}px solid ${t.c(`components.${g}.${border}`)}`,
        borderRadius: t.n(`components.${g}.radius`, 5),
        fontSize: t.n("typography.fontSize.sm", 12.6),
        color: value ? t.c(`components.${g}.${error ? "errorForeground" : "foreground"}`) : t.c(`components.${g}.placeholderForeground`),
        overflow: "hidden",
        whiteSpace: "nowrap",
      }}
    >
      {value || placeholder}
    </Part>
  )
}

export function Combo({ value }: { value: string }) {
  const t = useTok()
  return (
    <Part
      sel="components.themedComboBox.background"
      style={{
        ...flex({ justifyContent: "space-between", flex: 1, minWidth: 0 }),
        height: t.n("metrics.controlHeightSm", 26),
        boxSizing: "border-box",
        padding: "0 8px",
        background: t.c("components.themedComboBox.background"),
        border: `${t.n("components.themedComboBox.borderWidth", 1)}px solid ${t.c("components.themedComboBox.border")}`,
        borderRadius: t.n("components.themedComboBox.radius", 5),
        color: t.c("components.themedComboBox.foreground"),
        fontSize: t.n("typography.fontSize.sm", 12.6),
      }}
    >
      {value}
      <ChevronDown size={14} color={t.c("components.themedComboBox.icon")} />
    </Part>
  )
}

export function Chip({ label, kind }: { label: string; kind: "primary" | "secondary" | "outline" }) {
  const t = useTok()
  const bg = { primary: "primaryBackground", secondary: "secondaryBackground", outline: "outlineBackground" }[kind]
  const fg = { primary: "primaryForeground", secondary: "secondaryForeground", outline: "foreground" }[kind]
  return (
    <Part
      sel={`components.themedChip.${bg}`}
      style={{
        ...flex({ height: 20, padding: "0 8px" }),
        boxSizing: "border-box",
        background: t.c(`components.themedChip.${bg}`),
        color: t.c(`components.themedChip.${fg}`),
        border: `${t.n("components.themedChip.borderWidth", 1)}px solid ${t.c(`components.themedChip.${kind === "secondary" ? "secondaryBorder" : "border"}`)}`,
        borderRadius: t.n("components.themedChip.radius", 5),
        fontSize: t.n("typography.fontSize.xs", 11.5),
      }}
    >
      {label}
    </Part>
  )
}

export function SectionHeader({ children }: { children: ReactNode }) {
  const t = useTok()
  return (
    <Part
      sel="components.collapsibleSectionHeader.foreground"
      style={{
        ...flex({ gap: 6 }),
        paddingBottom: 4,
        color: t.c("components.collapsibleSectionHeader.foreground"),
        borderBottom: `${t.n("components.collapsibleSectionRule.borderWidth", 1)}px solid ${t.c("components.collapsibleSectionRule.border")}`,
        fontSize: t.n("typography.fontSize.sm", 12.6),
        fontWeight: t.n("typography.fontWeight.semibold", 600),
      }}
    >
      <ChevronDown size={12} color={t.c("components.collapsibleSectionHeader.icon")} />
      {children}
    </Part>
  )
}
