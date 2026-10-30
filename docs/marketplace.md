# Marketplace API

One API under `/api/v1/marketplace` for everything Drift can browse and install: Drift Assets, sound effects (SFX), stock media and community items. The manifest tells a client which pages and sections to show and which sources exist for it. Routes: `Api/Endpoints/MarketplaceEndpoints.cs`. Signing: `Api/Http/DriftClientAuth.cs`. Manifest, browse, installs and layout validation: `Application/Marketplace/`. Layout and flag types: `Domain/Marketplace/Marketplace.cs`. Admin editing: `Application/Admin/MarketplaceAdmin.cs`. The layout document is published as `docs/marketplace-layout.schema.json`, and `backend/openapi.json` covers the routes.

The old `/api/v1/stock/*` JSON endpoints (catalog, search, items, resolve, downloads, sfx, drift-assets) were removed once the web app moved to `/api/v1/marketplace/*`. Only the byte routes remain: `/stock/files/{token}`, `/stock/media/{token}` and `/stock/sfx/files/{area}/{path}`.

## Callers

| Caller | How it is identified | Distribution |
| --- | --- | --- |
| Signed Drift client | valid `X-Cutwire-Signature` (see below) | `X-Cutwire-Distribution`, lower-cased; missing or unknown values become `unknown` |
| Web app | `Sec-Fetch-Site: same-origin` | `web` |

Any other request gets `401 invalid_client` ("A signed Drift client or the web app is required."). Known distributions are the values of `Distributions.All`: `windows`, `windows-store`, `macos`, `appimage`, `flatpak`, `flathub`, `arch`, `android-github`, `android-play`, `source`, `web` and `unknown`.

The app version comes from `X-Cutwire-App: Drift/<semver> (<platform>)`. If it does not parse, the caller has no version, and version ranges do not exclude it.

## Request signing

Headers:

- `X-Cutwire-Client`: 64 hex characters, the client id.
- `X-Cutwire-Timestamp`: Unix seconds.
- `X-Cutwire-Nonce`: 8 to 64 characters, ASCII letters, digits, `-` or `_`.
- `X-Cutwire-Signature`: lower-case hex HMAC-SHA256.
- `X-Cutwire-App` and `X-Cutwire-Distribution` are not signed.

Canonical string, joined with `\n` and encoded as UTF-8:

```
timestamp \n nonce \n clientId \n METHOD \n target \n hex(sha256(body))
```

`target` is the raw request target (path and query) as sent. The HMAC key is the UTF-8 bytes of one entry in `DriftClient:Keys`. The server tries every key, which is how rotation works: list the old and new keys together, then remove the old one.

Configuration:

- `DriftClient:Keys`, an array. Environment form: `DriftClient__Keys__0`, `DriftClient__Keys__1`. An empty array makes every signed request fail.
- `DriftClient:MaxSkewSeconds`, default 300.

Checks, in order: header shape; timestamp within `MaxSkewSeconds` of server time; body hash; signature (fixed-time comparison); nonce not already used by this client. A timestamp outside the window gets a 401 with a `Date` header holding the server's time. A nonce is recorded only after its signature verifies, so forged requests cannot fill the cache.

Replay cache: an `IMemoryCache` entry per `clientId|nonce`, kept for `2 × MaxSkewSeconds`. It lives in each process, so a restart or a second instance accepts a replay that the first one would have refused.

Failures return `application/problem+json` with status 401 and `code: "invalid_client"`. The `detail` says which check failed. The middleware runs on every route, so a request that carries `X-Cutwire-Signature` and fails is rejected even outside `/marketplace`. Unsigned requests skip it and go to the caller check.

## Manifest

`GET /api/v1/marketplace` returns `{ schema: 1, features, sources, pages }`.

- `ETag` is a weak tag over the body. `Vary: X-Cutwire-Distribution, X-Cutwire-App`. `Cache-Control: private, no-cache`. `If-None-Match` gets a 304.
- Layouts and flags are read on every request. The `sources` set is cached per distribution for 60 seconds (`ManifestCache`), and admin saves clear it.

**Layout selection.** Enabled layouts that match the caller are tried in order of `Priority` (highest first), then `UpdatedAt` (newest first). The first one whose `pagesJson` validates is used. If none does, the embedded `default-layout.json` is used.

**Targeting** (layouts and flags alike). An empty distribution list matches every distribution. The version range is `[MinAppVersion, MaxAppVersion)`: the minimum is inclusive and the maximum is exclusive.

**Flags.** Matching flags are folded into `features` by key. They are applied from least to most specific (distribution list adds 4, each version bound adds 1), then oldest to newest, so the last one wins. A flag's value is `ValueJson` when it is set and parses; otherwise it is the boolean `Enabled`. `Enabled` is ignored when `ValueJson` is set, so a disabled flag that has a value still sends that value.

Flags the server reads:

- `community`: `false` hides every `community.*` source.
- `community.<kind>`: `false` hides that community source.
- `source.<id>`: `false` hides any source, for example `source.stock.video`.
- `resolve`: `false` turns off pasted-link resolve.

**Sources.** Keyed by id:

| id | kind | present when |
| --- | --- | --- |
| `drift-assets` | `drift-assets` | the Drift Assets library has categories |
| `sfx` | `sfx` | the SFX index has categories; `audio` media kind; a category's subcategories are its children with ids `cat/sub` |
| `stock.<type>` | `stock` | for each catalog type (`video`, `photo`, `audio`, `music`, `greenscreen`); `searchable` if any provider can search, `canResolve` if any can resolve; provider quota `remaining` is stripped |
| `community.<kind>` | `community` | for `effect`, `transition`, `audio-effect`, `theme`, `look` and `effect-template`, when enabled; sorts are `top`, `trending`, `new`, `downloads` |

**Pruning.** After selection, a section is dropped if its `source.id` is not in `sources`. A `chips` section is also dropped if the source of its target grid is missing. Pages left with no sections are dropped. `seeAll` and `open-page` actions that point at a dropped page are removed. Clients never receive a section whose source they cannot use.

## Layout document

A layout is `pages`: 1 to 12 pages, each with 1 to 30 sections. The page ids `item` and `downloads` are reserved: the web and Drift clients use them as fixed routes and tabs, so a layout that declares them is rejected (`reserved`). Section types: `hero`, `notice`, `shelf`, `grid`, `chips`, `stock-browser`, `sfx-browser`, `downloads`. `shelf`, `grid`, `chips`, `stock-browser` and `sfx-browser` are bound to a source. Keys that are not listed are rejected, so a typo in the admin editor fails loudly. URLs must be `https`. Limits are in `Application/Marketplace/LayoutDocument.cs` and the schema file.

## Listing

`GET /api/v1/marketplace/sources/{source}/items`

Query: `category`, `q`, `cursor`, `limit` (default 24, max 50), `sort`, `period`, `provider`. Any other query key is passed to stock as a filter. Response: `{ items, nextCursor, quota, warnings }`, with `Cache-Control: no-store`. An unknown or hidden source returns 404 `unknown_source`.

- `drift-assets` and `sfx`: the cursor is a base64url offset. A bad cursor returns 400 `invalid_cursor`. `q` must match every word against name, id, kind and category (for SFX: title, id, tags and category).
- `sfx` `category` is `cat` or `cat/sub`.
- `community.<kind>`: `sort` and `period` go to the community listing. Items whose `minAppVersion` is above the caller's version are dropped, so a page can be shorter than `limit`.
- `stock.<type>` with `provider=<id>`: a single provider search with its own cursor and `quota`.
- `stock.<type>` with no provider, or `provider=*`: fan-out. Each provider that can search and has no quota limit is searched with `limit / count` results (at least 1). Results are interleaved round-robin. The cursor is base64url JSON mapping each provider to its next cursor; a provider with no next cursor drops out. A provider that fails is left out and named in `warnings` as `providerId:code`. If every provider fails, the first error is returned. If no provider is eligible, one unscoped search runs. `quota` is null on a fan-out.

## Items

`GET /api/v1/marketplace/sources/{source}/items/{id}` returns one `MarketItemDto`. Relative URLs are made absolute against `PublicUrl`.

`extra` carries source-specific data:

- Drift Assets: `{ kind, categoryLabel }`.
- SFX: `{ peaks }` when computed (see below).
- Stock: `{ provider, downloadable, creator }`.
- Community: `{ score, downloads, owner, slug }` on the list, plus `version` and `minAppVersion` on the detail.

## Resolve

- `POST /api/v1/marketplace/resolve {url}` returns 202 with a job. `GET /resolve/{id}` polls it. Jobs are in memory, belong to the caller's install, and last 15 minutes.
- Returns 404 `resolve_unavailable` when the `resolve` flag is off or no stock source can resolve.
- Rate limited by `RateLimiting:StockWritePermitLimit` and `StockWriteWindow`.

## Installs

`POST /api/v1/marketplace/installs {source, itemId, variantId?}` returns 201 with an `InstallJobDto`. `GET /installs/{id}` polls it.

- `drift-assets`, `sfx` and `community.<kind>` are **ready** at once. The id is `ready_<guid>`, `status` is `ready`, `progress` is 1, and `files` lists the files (role `main`; for Drift Assets, the variant's files with `main` first, each with its sha256). Nothing is stored, and a `ready_` id is not polled, so `GET` returns 404 `install_not_found`.
- Community installs record a download and return `/files/{sha256}`. If the caller's version is below the item's `minAppVersion`, the response is 409 `app_too_old` with a `minAppVersion` extension.
- `stock.<type>` installs are **jobs**. The id is `stock_<jobId>` and the status, progress, file, license and error come from the download job. Quota and failure rules are those of the stock download queue, and a failed job refunds its slot.
- Non-default variants are rejected with 404 `variant_not_found` for SFX.

## SFX peaks and cache

`extra.peaks` on an SFX item is base64 of 64 bytes. The sound is cut into 64 equal slices of its first 300 seconds. Each slice is decoded by ffmpeg to mono 8 kHz s16le, and the peak is mapped onto a 48 dB range: `0` is silence or 48 dB or more below full scale, and `255` is full scale.

The SFX install's `sha256` is taken from the index when it has one, and computed from the library file otherwise.

Cache: `Stock:Sfx:CachePath` (a JSON file). Entries are keyed by library path and hold `{ stamp, peaks, sha256 }`, where `stamp` is `length:lastWriteTicks`. An entry whose stamp no longer matches is recomputed. The file loads once on first use (an unreadable file is rebuilt), is written through a `.tmp` file and a move, and is saved when the queue drains and at shutdown. At startup every sound without peaks or a hash is queued. Failures are remembered only in memory for the file's stamp, so they are retried after a restart. An empty path keeps the cache in memory only.

## Admin

Cookie session, admin role only (`AdminOnly`):

- `/admin/marketplace`: lists layouts and flags. The preview takes `distribution` and `version`, shows the manifest JSON a client would get, and names the layout chosen (or "built-in default").
- `/admin/marketplacelayout/{id?}`: name (unique, up to 100 characters), priority, enabled, distributions, version range, `pagesJson`. The same validator as the API runs on save.
- `/admin/marketplaceflag/{id?}`: key (`^[a-z0-9][a-z0-9.-]{0,63}$`), enabled, `valueJson`, distributions, version range, note (up to 500 characters).

Every save and delete writes an audit entry (`marketplace.layout.save|delete`, `marketplace.flag.save|delete`) and clears this process's source cache. Other instances keep their cache for up to 60 seconds.

## Adding a section type

A new type needs three things to land together:

1. **Server**: add the constant and entry in `LayoutDocument.SectionTypes` (and `SourceBound` if it takes a source), the allowed keys and checks in `LayoutValidator.Section`, and the same rules in `docs/marketplace-layout.schema.json`. If it needs a new source, or a dependency like `chips` has, update `MarketplaceManifest` (`SourcesAsync`, `Survives`) and `MarketplaceBrowse`.
2. **Drift** (QML) renderer.
3. **Web** renderer.

Clients skip section types they do not know, so an old client simply does not show the new section. Keep a new type out of the default layout until the clients that render it have shipped.

Tests: `backend/tests/CutWire.Studio.IntegrationTests/MarketplaceTests.cs` and `DriftClientAuthTests.cs` (unit).
