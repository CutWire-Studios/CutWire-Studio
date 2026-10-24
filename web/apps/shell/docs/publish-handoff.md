# Publish handoff (Forge / Theme Maker -> `/community/upload`)

Tools never upload on their own. They park the package (and any images they could render) in
IndexedDB and navigate to `/community/upload?kind=<kind>`. The upload wizard reads the record,
pre-fills the package, title, description and media slots, and deletes the record when the item is
published or the user discards it. All apps share one origin, so they share the database.

Reader: `src/community/lib/handoff.ts` (`readHandoff`, `parseHandoff`, `clearHandoff`).

## Where

| | |
|---|---|
| Database | `cutwire-handoff` (no explicit version; the reader creates the store if missing) |
| Object store | `handoff` (out-of-line keys, structured clone; the `idb-keyval` `createStore` layout) |
| Key | `studio:publish-handoff` |

One slot. A new publish overwrites the previous one. A missing record means "nothing to import".

## Unified shape

```ts
interface PublishHandoff {
  kind: string            // asset kind id: effect | transition | audio-effect | theme | look | effect-template
  packageBlob: Blob       // the .driftfx / .json / .zip / .cube ...
  filename: string        // e.g. "glitch-wipe.driftfx"
  thumbnailBlob?: Blob    // role "thumbnail" (512x288 WebP when exact)
  previewStripBlob?: Blob // role "preview-strip" (transition, 1536x128 WebP when exact)
  screenshotBlob?: Blob   // role "screenshot" (theme, 1280x720 WebP when exact)
  title?: string
  description?: string
  source?: string         // "forge" | "themes", only used for the banner text
}
```

Images that already meet the media spec are uploaded to the draft automatically when the user reaches
the Media step. Images that do not (wrong size, too many bytes, not WebP) open the in-browser fixer.
For kinds with a `thumbnail` spec but only a `screenshotBlob`, the screenshot is offered to the
fixer as the source for the thumbnail.

## Legacy Theme Maker record (still read)

`apps/themes/docs/publish-handoff.md` writes `version: 1` records under the key `theme-upload` in the
same database and store. The reader accepts them and maps them onto the unified shape:

| Theme Maker | Unified |
|---|---|
| `package.blob` / `package.fileName` | `packageBlob` / `filename` |
| `screenshot.blob` | `screenshotBlob` |
| `meta.name` | `title` |
| `source` | `source` |

Lookup order is the unified key first, then `theme-upload`. When `?kind=` is given only a record of
that kind is used. Theme Maker can migrate to the unified key at any time; no coordination is needed.

## Trust

The record comes from the user's browser. The wizard re-checks extension, size and media, and the
server repeats every check.
