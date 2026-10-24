import { toCss, type Json, type JsonObject, type TokenEntry } from "@cutwire/drift-theme"
import { Input, Select, Switch } from "@cutwire/ui"
import { useState } from "react"
import { isRef } from "@/lib/tokens"
import { ColorLiteral, Swatch } from "./color"
import { display, RefPicker } from "./refPicker"

const RANGES: Record<string, [number, number, number]> = {
  "window.backgroundOpacity": [0, 1, 0.01],
  "motion.pressScale": [0.5, 1.2, 0.01],
}

function NumberInput({ value, unit, onChange, step = "any" }: { value: number; unit?: string; onChange(n: number): void; step?: string | number }) {
  return (
    <div className="flex items-center gap-1">
      <Input type="number" step={step} className="font-mono" value={Number.isFinite(value) ? value : 0} onChange={(e) => e.target.value !== "" && onChange(+e.target.value)} />
      {unit && <span className="w-6 text-xs text-muted-foreground">{unit}</span>}
    </div>
  )
}

interface LiteralProps {
  token: TokenEntry
  /** The value to edit, with references already resolved. */
  value: Json | undefined
  onChange(v: Json): void
}

function NumberLiteral({ token, value, onChange }: LiteralProps) {
  const range = RANGES[token.path]
  const n = typeof value === "number" ? value : 0
  return (
    <div className="flex flex-col gap-2">
      <NumberInput value={n} unit={token.unit} onChange={onChange} />
      {range && <input type="range" min={range[0]} max={range[1]} step={range[2]} value={n} className="accent-primary" onChange={(e) => onChange(+e.target.value)} />}
    </div>
  )
}

function PairLiteral({ token, value, onChange }: LiteralProps) {
  const p = (typeof value === "object" && value !== null && !Array.isArray(value) ? value : {}) as JsonObject
  const part = (k: "pointer" | "touch") => (
    <label className="flex flex-1 flex-col gap-1 text-xs text-muted-foreground">
      {k}
      <NumberInput value={typeof p[k] === "number" ? (p[k] as number) : 0} unit={token.unit} onChange={(n) => onChange({ pointer: 0, touch: 0, ...p, [k]: n })} />
    </label>
  )
  return (
    <div className="flex gap-2">
      {part("pointer")}
      {part("touch")}
    </div>
  )
}

function Literal(props: LiteralProps) {
  const { token, value, onChange } = props
  switch (token.type) {
    case "color":
      return <ColorLiteral value={typeof value === "string" ? value : "#000000"} onChange={onChange} />
    case "number":
      return <NumberLiteral {...props} />
    case "pair":
      return <PairLiteral {...props} />
    case "enum":
      return <Select value={String(value ?? token.options?.[0] ?? "")} onValueChange={onChange} options={(token.options ?? []).map((o) => ({ value: o, label: o }))} />
    case "boolean":
      return <Switch checked={value === true} onCheckedChange={onChange} aria-label={token.path} />
    default:
      return <Input value={typeof value === "string" ? value : display(value)} onChange={(e) => onChange(e.target.value)} />
  }
}

function ModeTabs({ mode, onMode }: { mode: "value" | "ref"; onMode(m: "value" | "ref"): void }) {
  return (
    <div className="inline-flex rounded-full bg-muted p-0.5 text-xs" role="tablist">
      {(["value", "ref"] as const).map((m) => (
        <button key={m} type="button" role="tab" aria-selected={mode === m} onClick={() => onMode(m)} className={`cursor-pointer rounded-full px-3 py-1 font-medium ${mode === m ? "bg-surface shadow-sm" : "text-muted-foreground"}`}>
          {m === "value" ? "Value" : "Reference"}
        </button>
      ))}
    </div>
  )
}

interface Common {
  self: string
  token: Pick<TokenEntry, "type" | "of" | "path" | "category" | "group">
  merged: JsonObject
  resolved: JsonObject
}

/** One value that can be a literal or a `$reference`. */
export function ValueOrRef({ common, raw, literal, onChange, entry }: { common: Common; raw: Json | undefined; literal: Json | undefined; onChange(v: Json): void; entry: TokenEntry }) {
  const refMode = isRef(raw)
  const [picking, setPicking] = useState(false)
  const mode = refMode || picking ? "ref" : "value"
  return (
    <div className="flex flex-col gap-3">
      {entry.type !== "boolean" && entry.type !== "enum" && entry.type !== "string" && entry.type !== "font" && (
        <ModeTabs
          mode={mode}
          onMode={(m) => {
            if (m === "ref") setPicking(true)
            else {
              setPicking(false)
              if (refMode && literal !== undefined) onChange(literal)
            }
          }}
        />
      )}
      {mode === "ref" ? (
        <>
          {refMode && <p className="break-all font-mono text-xs">{String(raw)}</p>}
          <RefPicker
            self={common.self}
            kind={common.token}
            merged={common.merged}
            resolved={common.resolved}
            current={refMode ? String(raw) : null}
            onPick={(r) => {
              setPicking(false)
              onChange(r)
            }}
          />
        </>
      ) : (
        <Literal token={entry} value={literal} onChange={onChange} />
      )}
    </div>
  )
}

/** A list of colours (palettes of category and keyframe ramp colours): each entry is literal or reference. */
export function ArrayEditor({ entry, raw, resolvedList, common, onChange }: { entry: TokenEntry; raw: Json | undefined; resolvedList: Json | undefined; common: Common; onChange(v: Json): void }) {
  const list = Array.isArray(raw) ? raw : []
  const resolved = Array.isArray(resolvedList) ? resolvedList : []
  const [open, setOpen] = useState<number | null>(null)
  const colorKind = { ...common.token, type: "color" as const, of: undefined }
  return (
    <ul className="flex flex-col gap-1">
      {list.map((item, i) => (
        <li key={i} className="flex flex-col gap-2">
          <button type="button" onClick={() => setOpen(open === i ? null : i)} className="flex cursor-pointer items-center gap-2 rounded-md px-2 py-1 text-left text-xs hover:bg-muted">
            <Swatch css={toCss(String(resolved[i] ?? ""))} className="size-5" />
            <span className="font-mono">{String(item)}</span>
          </button>
          {open === i && (
            <ValueOrRef
              entry={{ ...entry, type: "color", path: `${entry.path}[${i}]` }}
              common={{ ...common, token: colorKind }}
              raw={item}
              literal={resolved[i]}
              onChange={(v) => onChange(list.map((x, j) => (j === i ? v : x)))}
            />
          )}
        </li>
      ))}
    </ul>
  )
}
