import dark from "./generated/dark.json"
import frost from "./generated/frost.json"
import graphite from "./generated/graphite.json"
import light from "./generated/light.json"
import type { Library, ThemeDoc } from "./types"

/** The four themes Drift ships, as documents (references unresolved). */
export const builtins: Library = {
  dark: dark as ThemeDoc,
  light: light as ThemeDoc,
  graphite: graphite as ThemeDoc,
  frost: frost as ThemeDoc,
}
