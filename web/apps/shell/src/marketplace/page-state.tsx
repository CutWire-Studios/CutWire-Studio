import { createContext, useCallback, useContext, useMemo, useState, type ReactNode } from "react"

interface PageState {
  /** Selected category per grid section id. */
  categories: Record<string, string>
  setCategory: (grid: string, category: string) => void
}

const Ctx = createContext<PageState | null>(null)

/** Lets a chips section drive the grid it targets. */
export function PageStateProvider({ children }: { children: ReactNode }) {
  const [categories, setCategories] = useState<Record<string, string>>({})
  const setCategory = useCallback((grid: string, category: string) => setCategories((c) => ({ ...c, [grid]: category })), [])
  const value = useMemo(() => ({ categories, setCategory }), [categories, setCategory])
  return <Ctx.Provider value={value}>{children}</Ctx.Provider>
}

export function usePageState(): PageState {
  const v = useContext(Ctx)
  if (!v) throw new Error("usePageState needs a PageStateProvider")
  return v
}
