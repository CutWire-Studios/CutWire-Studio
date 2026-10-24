import { formatColor, parseColor, toCss, type Rgba } from "@cutwire/drift-theme"
import { Input } from "@cutwire/ui"
import { useState } from "react"

export function Swatch({ css, className = "size-8" }: { css: string; className?: string }) {
  return (
    <span
      className={`${className} inline-block shrink-0 rounded-md border border-border`}
      style={{ backgroundImage: `linear-gradient(${css}, ${css}), repeating-conic-gradient(#8884 0% 25%, transparent 0% 50%)`, backgroundSize: "auto, 10px 10px" }}
    />
  )
}

const hex6 = (c: Rgba) => formatColor({ ...c, a: 255 }, "qt")

/** Colour literal editor: picker, alpha, and a `#AARRGGBB` text field (alpha first, as Drift reads it). */
export function ColorLiteral({ value, onChange }: { value: string; onChange(v: string): void }) {
  const parsed = parseColor(value) ?? { r: 0, g: 0, b: 0, a: 255 }
  const [draft, setDraft] = useState<string | null>(null)
  const text = draft ?? value
  const commit = (c: Rgba) => onChange(formatColor(c, "qt"))
  const bad = parseColor(text) === null
  return (
    <div className="flex flex-col gap-2">
      <div className="flex items-center gap-2">
        <label className="relative">
          <Swatch css={toCss(value)} className="size-10" />
          <input
            type="color"
            aria-label="Pick colour"
            className="absolute inset-0 size-full cursor-pointer opacity-0"
            value={hex6(parsed)}
            onChange={(e) => {
              const c = parseColor(e.target.value)
              if (c) commit({ ...c, a: parsed.a })
            }}
          />
        </label>
        <Input
          aria-label="Colour value"
          aria-invalid={bad}
          className="font-mono"
          value={text}
          spellCheck={false}
          onChange={(e) => {
            setDraft(e.target.value)
            const c = parseColor(e.target.value)
            if (c && /^#/.test(e.target.value.trim())) commit(c)
          }}
          onBlur={() => setDraft(null)}
        />
      </div>
      <label className="flex items-center gap-2 text-xs text-muted-foreground">
        <span className="w-10">Alpha</span>
        <input type="range" min={0} max={255} value={parsed.a} className="flex-1 accent-primary" onChange={(e) => commit({ ...parsed, a: +e.target.value })} />
        <span className="w-8 text-right font-mono">{parsed.a}</span>
      </label>
      <p className="font-mono text-[11px] text-muted-foreground">{formatColor(parsed, "css")}</p>
    </div>
  )
}
