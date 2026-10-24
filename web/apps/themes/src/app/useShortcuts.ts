import { useEffect } from "react"
import { exportFile } from "@/features/toolbar/Toolbar"
import { SEARCH_ID } from "@/features/navigator/Navigator"
import { redo, undo } from "@/state/editor"

function typingInField(e: KeyboardEvent) {
  return (e.target as HTMLElement).closest("input, textarea, select, [contenteditable]") !== null
}

/** Ctrl+Z / Ctrl+Shift+Z undo and redo, Ctrl+S exports, `/` jumps to the token search. */
export function useShortcuts(enabled: boolean) {
  useEffect(() => {
    if (!enabled) return
    const onKey = (e: KeyboardEvent) => {
      const mod = e.ctrlKey || e.metaKey
      const key = e.key.toLowerCase()
      if (mod && key === "s") {
        e.preventDefault()
        exportFile("json")
        return
      }
      if (typingInField(e)) return
      if (mod && key === "z") {
        e.preventDefault()
        if (e.shiftKey) redo()
        else undo()
      } else if (mod && key === "y") {
        e.preventDefault()
        redo()
      } else if (!mod && e.key === "/") {
        e.preventDefault()
        document.getElementById(SEARCH_ID)?.focus()
      }
    }
    window.addEventListener("keydown", onKey)
    return () => window.removeEventListener("keydown", onKey)
  }, [enabled])
}
