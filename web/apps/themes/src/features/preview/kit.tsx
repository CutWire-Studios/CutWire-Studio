import { getPath, isPair, toCss, type JsonObject } from "@cutwire/drift-theme"
import { createContext, createElement, useContext, useMemo, type CSSProperties, type ReactNode } from "react"

export interface PreviewContext {
  /** The fully resolved theme document. */
  doc: JsonObject
  selected: string | null
  select(path: string): void
}

const Ctx = createContext<PreviewContext>({ doc: {}, selected: null, select: () => {} })

export const PreviewProvider = Ctx.Provider

export interface Tok {
  /** A colour token as CSS. */
  c(path: string, fallback?: string): string
  /** A numeric token; `{pointer, touch}` pairs give the pointer value. */
  n(path: string, fallback?: number): number
  s(path: string, fallback?: string): string
  b(path: string): boolean
  selected: string | null
  select(path: string): void
}

export function useTok(): Tok {
  const { doc, selected, select } = useContext(Ctx)
  return useMemo<Tok>(
    () => ({
      c: (p, fb = "transparent") => {
        const v = getPath(doc, p)
        return typeof v === "string" ? toCss(v) : fb
      },
      n: (p, fb = 0) => {
        const v = getPath(doc, p)
        if (typeof v === "number") return v
        if (isPair(v) && typeof v.pointer === "number") return v.pointer
        return fb
      },
      s: (p, fb = "") => {
        const v = getPath(doc, p)
        return typeof v === "string" ? v : fb
      },
      b: (p) => getPath(doc, p) === true,
      selected,
      select,
    }),
    [doc, selected, select],
  )
}

export interface PartProps {
  /** The token the inspector opens when this part is clicked. */
  sel?: string
  as?: string
  style?: CSSProperties
  className?: string
  children?: ReactNode
  title?: string
}

/** A piece of the mock window. Clicking it selects its main token. */
export function Part({ sel, as = "div", style, className, children, title }: PartProps) {
  const { selected, select } = useContext(Ctx)
  const props: Record<string, unknown> = { style, className, title }
  if (sel) {
    props["data-tok"] = sel
    props["data-selected"] = selected === sel
    props.onClick = (e: { stopPropagation(): void }) => {
      e.stopPropagation()
      select(sel)
    }
  }
  return createElement(as, props, children)
}

export const flex = (extra?: CSSProperties): CSSProperties => ({ display: "flex", alignItems: "center", ...extra })
