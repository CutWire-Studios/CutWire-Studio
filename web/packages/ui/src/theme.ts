import { useSyncExternalStore } from "react"

export type Theme = "light" | "dark" | "system"

export const THEME_KEY = "cw-theme"
const listeners = new Set<() => void>()

function stored(): Theme {
  try {
    const value = localStorage.getItem(THEME_KEY)
    if (value === "light" || value === "dark") return value
  } catch {
    // Private windows and blocked site data both throw here; system is a fine answer.
  }
  return "system"
}

let theme: Theme = stored()
let media: MediaQueryList | undefined

function systemDark() {
  media ??= window.matchMedia("(prefers-color-scheme: dark)")
  return media.matches
}

export function getTheme(): Theme {
  return theme
}

export function resolvedTheme(): "light" | "dark" {
  if (theme !== "system") return theme
  return systemDark() ? "dark" : "light"
}

export function setTheme(next: Theme) {
  theme = next
  const root = document.documentElement
  if (next === "system") delete root.dataset.theme
  else root.dataset.theme = next
  try {
    if (next === "system") localStorage.removeItem(THEME_KEY)
    else localStorage.setItem(THEME_KEY, next)
  } catch {
    // The choice still applies for this page view.
  }
  listeners.forEach((l) => l())
}

export function cycleTheme() {
  setTheme(resolvedTheme() === "dark" ? "light" : "dark")
}

export function initTheme() {
  theme = stored()
  if (theme !== "system") document.documentElement.dataset.theme = theme
  systemDark()
  media?.addEventListener("change", () => listeners.forEach((l) => l()))
}

export function useResolvedTheme(): "light" | "dark" {
  return useSyncExternalStore(
    (cb) => {
      listeners.add(cb)
      return () => listeners.delete(cb)
    },
    resolvedTheme,
  )
}
