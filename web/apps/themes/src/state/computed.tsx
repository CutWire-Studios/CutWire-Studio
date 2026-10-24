import {
  builtins,
  mergeLayers,
  resolveTheme,
  ThemeError,
  validateTheme,
  type MergedTheme,
  type ResolvedTheme,
  type ThemeDoc,
  type ValidationResult,
} from "@cutwire/drift-theme"
import { createContext, useContext, useDeferredValue, useMemo, type ReactNode } from "react"
import { useEditor } from "./editor"

export interface Computed {
  doc: ThemeDoc
  resolved: ResolvedTheme
  /** The same theme with nothing overridden: the base that "reset" returns to. */
  base: MergedTheme
  validation: ValidationResult
  /** Set when the document could not be resolved as written (unknown or cyclic `extends`). */
  error: string | null
}

export function compute(doc: ThemeDoc, side: "dark" | "light"): Computed {
  let error: string | null = null
  let usable = doc
  let resolved: ResolvedTheme
  try {
    resolved = resolveTheme(doc, builtins, { variant: side })
  } catch (e) {
    if (!(e instanceof ThemeError)) throw e
    error = e.message
    const { extends: _dropped, ...rest } = doc
    usable = rest
    resolved = resolveTheme(usable, builtins, { variant: side })
  }
  const identity: ThemeDoc = {}
  for (const k of ["name", "extends", "variant"]) if (k in usable) identity[k] = usable[k] as never
  const base = mergeLayers(identity, builtins, { variant: side })
  return { doc, resolved, base, validation: validateTheme(doc), error }
}

const Ctx = createContext<Computed | null>(null)

export function ComputedProvider({ children }: { children: ReactNode }) {
  const doc = useEditor((s) => s.doc)
  const side = useEditor((s) => s.side)
  const deferred = useDeferredValue(doc)
  const value = useMemo(() => compute(deferred, side), [deferred, side])
  return <Ctx.Provider value={value}>{children}</Ctx.Provider>
}

export function useComputed(): Computed {
  const v = useContext(Ctx)
  if (!v) throw new Error("ComputedProvider is missing")
  return v
}
