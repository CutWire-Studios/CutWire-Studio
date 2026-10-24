import type { Category } from "@cutwire/drift-theme"
import { create } from "zustand"

export type View = "start" | "editor"
export type SideTab = "inspector" | "contrast" | "issues"

interface UiState {
  view: View
  selected: string | null
  category: Category
  search: string
  modifiedOnly: boolean
  /** `category/group` keys of expanded navigator groups. */
  expanded: Record<string, true>
  dialogOpen: boolean
  sideTab: SideTab
  setSideTab(t: SideTab): void
  setView(v: View): void
  select(path: string | null, group?: string): void
  setCategory(c: Category): void
  setSearch(s: string): void
  setModifiedOnly(b: boolean): void
  toggleGroup(key: string): void
  setDialogOpen(b: boolean): void
}

export const useUi = create<UiState>((set) => ({
  view: "start",
  selected: null,
  category: "palette",
  search: "",
  modifiedOnly: false,
  expanded: {},
  dialogOpen: false,
  sideTab: "inspector",
  setSideTab: (sideTab) => set({ sideTab }),
  setView: (view) => set({ view }),
  select: (path, group) =>
    set((s) => {
      if (!path) return { selected: null }
      const category = path.split(".")[0] as Category
      return { selected: path, category, sideTab: "inspector" as SideTab, expanded: group ? { ...s.expanded, [`${category}/${group}`]: true } : s.expanded }
    }),
  setCategory: (category) => set({ category }),
  setSearch: (search) => set({ search }),
  setModifiedOnly: (modifiedOnly) => set({ modifiedOnly }),
  toggleGroup: (key) =>
    set((s) => {
      const expanded = { ...s.expanded }
      if (expanded[key]) delete expanded[key]
      else expanded[key] = true
      return { expanded }
    }),
  setDialogOpen: (dialogOpen) => set({ dialogOpen }),
}))
