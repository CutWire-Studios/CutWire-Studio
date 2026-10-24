# Publish handoff (Theme Maker -> Community upload)

"Publish to Community" does not upload anything itself. It stores a package and a screenshot in
IndexedDB, then navigates to `/community/upload?kind=theme`. The upload page reads the record,
shows it for the user to confirm and fill in title, tags and licence, and submits it to the API.
Both apps are served from one origin, so they share the same IndexedDB.

## Where

| | |
|---|---|
| Database | `cutwire-handoff` |
| Object store | `handoff` |
| Key | `theme-upload` |
| Format | `idb-keyval` layout: out-of-line key, value stored with structured clone |

Reading with `idb-keyval`:

```ts
import { createStore, get, del } from "idb-keyval"
const store = createStore("cutwire-handoff", "handoff")
const handoff = await get<ThemeHandoff>("theme-upload", store)
// ...after a successful upload, or when the user discards it:
await del("theme-upload", store)
```

There is a single slot. A new publish overwrites the previous one. The upload page should delete
the record once it has been submitted or discarded, and should treat a missing record as "nothing
to import" (for example when `?kind=theme` is opened directly).

## Shape (`version: 1`)

```ts
interface ThemeHandoff {
  version: 1
  kind: "theme"
  source: "themes"
  createdAt: number // Date.now()
  meta: {
    name: string // theme name; already checked against the reserved-name and charset rules
    variant: "dark" | "light"
    extends: string | null // "dark" | "light" | "graphite" | "frost" | null
    schemaVersion: 1
    dual: boolean // true when the package is a zip with theme.dark.json / theme.light.json
  }
  package: {
    fileName: string // "<slug>.json", or "<slug>.zip" when dual
    mime: "application/json" | "application/zip"
    blob: Blob // overrides only (minimal diff against the base); never contains qml/
  }
  screenshot: {
    fileName: "screenshot.webp"
    mime: "image/webp"
    width: 1280
    height: 720
    blob: Blob // at most 120,000 bytes
  }
}
```

The same type is exported from `web/apps/themes/src/services/publish.ts` (`ThemeHandoff`).

## Guarantees and what the server must still check

- The package passed `validateTheme` from `@cutwire/drift-theme` (JSON Schema 2020-12, reserved
  names, `schemaVersion` 1, name charset, no unresolved or cyclic references) before the handoff was
  written. The server repeats those checks and the zip limits (2 MB per entry, 8 MB total, 64
  entries).
- The screenshot is a WebP at exactly 1280x720 and under 120,000 bytes. The server enforces its
  own cap (120 KB) and decodes the image to confirm the size.
- The record is data from the user's browser. Treat it as untrusted input.

## Query string

`/community/upload?kind=theme` is the only parameter. The key is fixed, so no id is passed.
