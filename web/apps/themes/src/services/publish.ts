import { diffAgainstBase, exportJson, exportZip, builtins, splitVariants, type ThemeDoc } from "@cutwire/drift-theme"
import { createStore, set } from "idb-keyval"

/** The community upload page reads this database/store/key; see docs/publish-handoff.md. */
export const HANDOFF_DB = "cutwire-handoff"
export const HANDOFF_STORE = "handoff"
export const HANDOFF_KEY = "theme-upload"
export const SCREENSHOT_CAP_BYTES = 120_000
export const SCREENSHOT_SIZE = { width: 1280, height: 720 } as const
export const UPLOAD_URL = "/community/upload?kind=theme"

export interface ThemeHandoff {
  version: 1
  kind: "theme"
  source: "themes"
  createdAt: number
  meta: {
    name: string
    variant: "dark" | "light"
    extends: string | null
    schemaVersion: number
    dual: boolean
  }
  package: { fileName: string; mime: string; blob: Blob }
  screenshot: { fileName: string; mime: "image/webp"; width: 1280; height: 720; blob: Blob }
}

export interface ThemePackage {
  fileName: string
  mime: string
  blob: Blob
}

const slug = (name: string) => name.trim().toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-|-$/g, "") || "theme"

function stem(doc: ThemeDoc): string {
  return slug(String(doc.name ?? "theme"))
}

export function buildJsonFile(doc: ThemeDoc): ThemePackage {
  return { fileName: `${stem(doc)}.json`, mime: "application/json", blob: new Blob([exportJson(diffAgainstBase(doc, builtins))], { type: "application/json" }) }
}

/** theme.json plus theme.dark.json / theme.light.json when the theme has both overlays. Never a qml/ folder. */
export function buildZipFile(doc: ThemeDoc): ThemePackage {
  const { doc: body, sides } = splitVariants(diffAgainstBase(doc, builtins))
  const zip = exportZip(body, sides)
  return { fileName: `${stem(doc)}.zip`, mime: "application/zip", blob: new Blob([zip as Uint8Array<ArrayBuffer>], { type: "application/zip" }) }
}

/** Overrides only (vs. the base): a .json, or a .zip when the theme carries dark/light overlays. */
export function buildPackage(doc: ThemeDoc): ThemePackage {
  return diffAgainstBase(doc, builtins).variants ? buildZipFile(doc) : buildJsonFile(doc)
}

export function buildHandoff(doc: ThemeDoc, screenshot: Blob): ThemeHandoff {
  const pkg = buildPackage(doc)
  return {
    version: 1,
    kind: "theme",
    source: "themes",
    createdAt: Date.now(),
    meta: {
      name: String(doc.name ?? ""),
      variant: doc.variant === "light" ? "light" : "dark",
      extends: typeof doc.extends === "string" ? doc.extends : null,
      schemaVersion: 1,
      dual: pkg.fileName.endsWith(".zip"),
    },
    package: pkg,
    screenshot: { fileName: "screenshot.webp", mime: "image/webp", width: 1280, height: 720, blob: screenshot },
  }
}

export async function writeHandoff(handoff: ThemeHandoff): Promise<void> {
  await set(HANDOFF_KEY, handoff, createStore(HANDOFF_DB, HANDOFF_STORE))
}

export function download(fileName: string, blob: Blob) {
  const url = URL.createObjectURL(blob)
  const a = document.createElement("a")
  a.href = url
  a.download = fileName
  a.click()
  setTimeout(() => URL.revokeObjectURL(url), 1000)
}
