import { Moon, Sun } from "lucide-react"
import { cycleTheme, useResolvedTheme } from "../theme"
import { IconButton } from "./button"

export function ThemeToggle() {
  const dark = useResolvedTheme() === "dark"
  return (
    <IconButton label={dark ? "Switch to light mode" : "Switch to dark mode"} onClick={cycleTheme} className="size-11">
      {dark ? <Sun className="size-5" aria-hidden /> : <Moon className="size-5" aria-hidden />}
    </IconButton>
  )
}
