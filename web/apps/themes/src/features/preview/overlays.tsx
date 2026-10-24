import { CheckCircle2 } from "lucide-react"
import { Btn, Field } from "./controls"
import { flex, Part, useTok } from "./kit"

export function Toast() {
  const t = useTok()
  return (
    <Part
      sel="components.toastHost.background"
      style={{
        ...flex({ gap: 10, padding: "10px 14px" }),
        position: "absolute",
        right: 20,
        bottom: 296,
        borderRadius: t.n("components.toastHost.radius", 10),
        background: t.c("components.toastHost.background"),
        border: `${t.n("components.toastHost.borderWidth", 1)}px solid ${t.c("components.toastHost.border")}`,
        borderLeft: `${t.n("components.toastHost.accentWidth", 3)}px solid ${t.c("components.toastHost.accentSuccess")}`,
        color: t.c("components.toastHost.foreground"),
        fontSize: t.n("typography.fontSize.sm", 12.6),
        boxShadow: "0 6px 20px #0006",
      }}
    >
      <CheckCircle2 size={16} color={t.c("components.toastHost.accentSuccess")} />
      Project saved
    </Part>
  )
}

export function Dialog() {
  const t = useTok()
  return (
    <Part sel="components.themedDialog.scrim" style={{ ...flex({ justifyContent: "center" }), position: "absolute", inset: 0, background: t.c("components.themedDialog.scrim") }}>
      <Part
        sel="components.themedDialog.background"
        style={{
          width: t.n("metrics.dialog.widthSm", 340) + 80,
          boxSizing: "border-box",
          padding: 18,
          display: "flex",
          flexDirection: "column",
          gap: 12,
          background: t.c("components.themedDialog.background"),
          border: `${t.n("components.themedDialog.borderWidth", 1)}px solid ${t.c("components.themedDialog.border")}`,
          borderRadius: t.n("components.themedDialog.radius", 10),
          color: t.c("components.themedDialog.foreground"),
          boxShadow: "0 18px 60px #0008",
        }}
      >
        <Part sel="components.themedDialog.foreground" style={{ fontSize: t.n("typography.fontSize.base", 14.7) + 2, fontWeight: 600 }}>
          Export project
        </Part>
        <Part sel="semantic.mutedForeground" style={{ color: t.c("semantic.mutedForeground"), fontSize: t.n("typography.fontSize.sm", 12.6) }}>
          Choose a name for the exported file.
        </Part>
        <Field value="summer-trip.mp4" focus />
        <div style={flex({ gap: 8, justifyContent: "flex-end" })}>
          <Btn label="Cancel" />
          <Btn label="Export" kind="primary" />
        </div>
      </Part>
    </Part>
  )
}
