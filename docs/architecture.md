# CutWire Studio architecture

## Context
CutWire runs two separate web apps today:
- **Drift Forge** (`CutWire-Tinker`: React 19, Bun/Hono). It only stores effects in the browser's IndexedDB, so nothing can be shared or rated. Its MCP server works on throwaway server-side copies, so it can't edit a user's real project.
- **CutWire Marketplace** (`CutWire-Marketplace`: Vue 3, Go/PocketBase). It serves stock media, SFX and Drift Assets through an API that is hard to extend.

Studio replaces both with one "workspace" site. It has a launcher for creative tools, a community hub where people share content (with votes, mostly automatic moderation and strict quality gates), Forge with a cloud library and an MCP that works, a new Drift Theme Maker, and space reserved for Drift Web and future tools (music maker, etc.). The old marketplace stays live for older Drift clients. Its API is not reused; the new API is designed from scratch.

## Decisions (confirmed)
| Topic | Choice |
|---|---|
| Backend | .NET 10, Clean Architecture, self-contained **single-file** publish (not Native AOT, because EF Core and OpenIddict aren't AOT-ready). SPAs embedded as static files. |
| Frontend | React 19 + Vite, pnpm workspace. Each tool is its own SPA under a path prefix (micro-frontends), sharing `@cutwire/ui`. |
| Auth | Built-in ASP.NET Identity + **OpenIddict** (Studio is its own OIDC provider). Fresh start; Rauthy is not migrated. |
| Data | PostgreSQL 17 (metadata), EF Core + Npgsql. |
| Blobs | Content-addressed store on local disk (`sha256` path) behind `IBlobStore`. Cloudflare edge caches it via immutable URLs; no R2. |
| Routing | Path-based on one origin. |
| Forge server | AI proxy and MCP ported to .NET. |
| Scope | Everything in the brief, built by subagents, with conventional commits along the way. |

### Size assessment (drove the storage choice)
- Bundled Drift effects: median 20 KB uncompressed, max 572 KB (an embedded LUT PNG).
- Thumbnails: about 12.8 KB WebP. Transition strips: about 65 KB.
- Full theme JSON: 146 KB (about 15 KB zstd). Typical `extends` theme: about 2 KB.
- So disk is enough. Per-kind caps keep it bounded and act as a quality gate.

## URL map (one origin, future-proof)
```
/                 launcher (tool tiles from the app registry)
/community/...    browse, item pages, profiles, upload, moderation (/community/mod)
/forge/...        Forge (effect | transition | sound effect makers via ?kind=)
/themes/...       Drift Theme Maker
/edit/...         RESERVED: Drift Web (placeholder page; COOP/COEP headers already configured for this prefix)
/stock/...        Stock media, SFX library, Drift Assets (ex-marketplace browsing)
/account/...      sign in/up, profile, sessions, tokens, connected devices
/api/v1/...       public REST API (OpenAPI generated)
/connect/...      OpenIddict endpoints (authorize, token, device, userinfo, jwks)
/mcp              Forge MCP (streamable HTTP, OAuth 2.1 protected)
/files/{sha256}   blob downloads (immutable, Range support)
```
**Adding a tool** means: one entry in `web/packages/apps/registry.ts`, one SPA under `web/apps/<id>`, and one `AppHost` entry on the backend (prefix, SPA fallback, optional headers). The `/edit` and `music` entries ship as `status: "coming-soon"`.

## Repository layout (`/home/suhasdissa/Projects/CutWire-Studio`, `git init`)
```
backend/
  CutWire.Studio.sln
  src/Domain/          entities, value objects, domain rules (no deps)
  src/Application/     use cases (CQRS-style handlers), interfaces (IBlobStore, IClock, IContentGate...), validation
  src/Infrastructure/  EF Core/Npgsql, migrations, blob store, OpenIddict/Identity, email (SMTP/Resend), stock providers, Jint forge-core runtime, background jobs
  src/Api/             ASP.NET host: minimal-API endpoints, MCP server, SPA hosting, OpenAPI, rate limiting
  tests/               Domain/Application unit tests, Api integration tests (Testcontainers Postgres)
web/                   pnpm workspace
  packages/ui          design system: tokens ported from CutWire/app/assets/css/tailwind.css, Tailwind v4, Radix-based components (React analogue of shadcn), AppHeader with app switcher, theme toggle (`cw-theme` key)
  packages/api-client  generated from backend OpenAPI (openapi-typescript + fetch wrapper)
  packages/apps        app registry (id, name, path, icon, status, description)
  packages/forge-core  moved from CutWire-Tinker/src/core (pure TS: doc, edit, nodes, compiler, export, ai tools)
  apps/shell           launcher + account + community + stock (one SPA, routes /, /community, /account, /stock)
  apps/forge           moved from CutWire-Tinker/src (features, services, state)
  apps/themes          Drift Theme Maker
  apps/edit            placeholder ("Drift Web, coming soon")
deploy/Dockerfile      multi-stage: pnpm build -> dotnet publish single-file -> runtime image (with ffmpeg + yt-dlp)
docs/                  architecture.md, api.md, quality-gates.md, asset-kinds.md
```

## Backend design

### Extensible asset kinds (the core abstraction)
`IAssetKind` handler, one per kind, registered in DI. Each handler has:
- `Id` (`effect`, `transition`, `audio-effect`, `theme`, `look`, `effect-template`; later `language-pack`, `project-template`, ...)
- `AcceptedPackageFormats`, `MaxPackageBytes`
- `Validate(package) -> PackageInfo` (parsed manifest, files, `minAppVersion`, Drift install kind)
- `MediaSpecs` (required and optional images, with exact sizes)
- `ExtractSearchText`

Adding LUTs, templates or language packs is a new handler plus a frontend card renderer. No schema change is needed.

Initial handlers:
- **effect / transition / audio-effect**: parse `.driftfx`. Check the `DRIFTFX\0` magic, the canonical manifest, per-file sha256 and the trailing digest, using ZstdSharp. Check that `provides[0].kind` matches. Run a manifest schema check per kind, using the field lists from `CutWire-Tinker/src/core/compiler/manifest.ts` and `VideoEd/docs/gpu-effects.md`. Caps: 2 MB packed and 8 MB unpacked.
- **theme**: `.json` or `.zip` validated against `VideoEd/resources/themes/theme.schema.json`. A `qml/` folder is **rejected** (QML overrides are code). Reserved names are rejected. Same zip limits as Drift (2 MB per entry, 8 MB total, 64 entries).
- **look**: a `.cube` 3D LUT (size ≤ 65) plus metadata. Cap 4 MB.
- **effect-template**: `template.json` that references effect ids.

### Data model (Postgres)
- **Identity**: `users` (Identity), `user_profiles` (handle, display name, avatar blob, bio, `trust_level`, `created_at`, `banned_until`), OpenIddict tables, `personal_access_tokens`.
- **Catalog**:
  - `items` (id, kind, slug, owner, title, summary, description, `license`, `status`: draft/published/hidden/removed, `visibility_reason`, `score_up`, `score_down`, `wilson_score`, `hot_score`, `downloads_total`, `search_vector` tsvector, `kind_meta` jsonb, timestamps)
  - `item_versions` (semver, changelog, `package_blob` sha256, size, `min_app_version`, `manifest` jsonb)
  - `item_media` (role: thumbnail/strip/gallery, `blob`, w, h, bytes, phash)
  - `tags` (curated vocabulary per kind), `item_tags`
- **Social**: `votes` (item, user, ±1, unique), `reports` (item, reporter, reason enum, note, status), `download_events` (rolled up nightly into `item_download_daily`), `follows` (optional, later).
- **Moderation**: `moderation_actions` (audit log: actor, target, action, reason, auto/manual), `cleanup_schedule` (item, purge_at).
- **Forge**: `forge_documents` (owner, local id, kind, name, `doc` jsonb, `revision` int, `thumb` blob, `updated_at`), `forge_doc_assets` (doc assets moved out of base64 into blobs, deduped by sha256), `ai_usage` (day, user, neurons).
- **Stock (ex-marketplace)**: `provider_settings`, `download_jobs`, `quota_events`, `asset_categories`, `sfx_categories`, with the same semantics as `CutWire-Marketplace/backend/internal/store/*.go`. Coins are dropped.
- **Blobs**: `blobs` (sha256 PK, size, mime, `ref_count`). A GC job deletes files whose ref_count is 0 after 7 days.

### Quality gates (automatic, run before publish; `IContentGate` pipeline, config in `appsettings`)
- **Thumbnail**:
  - WebP only. Exact size per kind: effect, look and template 512×288; transition strip 1536×128, matching Drift; theme 1280×720 screenshot.
  - Max size: thumbnail 40 KB, strip 80 KB, theme screenshot 120 KB. This is set below today's averages for the canvas sizes, so users have to compress.
  - Decoded server-side with SkiaSharp to verify the real dimensions. Images that are near-blank or mostly one solid colour are rejected (luma variance check).
- **Title**:
  - ASCII printable only, 6–60 characters, at least 2 words or one dictionary word of 4+ characters.
  - Rejected: all caps; URLs; repeated characters (`!!!!`, `aaaa`); a title that duplicates the owner's other items.
- **Summary** (required): 20–140 characters. **Description** (required): 80–4000 characters, ASCII only (no emoji, no non-Latin text).
- **English ratio**: at least 75% of tokens must be in a bundled English word list (SCOWL size-70, embedded resource, about 1 MB), after removing numbers, known technical terms (`glsl`, `lut`, `vhs`...) and the item's own tag words.
- **Profanity / spam**: a bundled denylist and link-count limits.
- **Tags**: 1–5, from the curated vocabulary for the kind. Category is required. License is required (from an SPDX subset).
- **Package**: kind-specific validation (see above).
- **Duplicates**: an exact sha256 match of the package is rejected. A thumbnail perceptual hash within Hamming distance 6 of an existing item is flagged.
- **Account**:
  - Email must be verified.
  - Rate limits by trust level: T0 (new) 3 uploads/day, T1 10/day, T2 30/day.
- Gate errors come back as RFC 7807 problem+json with field-level `errors[]`. The upload form runs the same rules on the client (shared rule definitions are exported via `/api/v1/meta/quality-rules`).

### Votes, ranking, moderation
- **Votes**: up or down; one per user per item; not allowed on your own items. T0 votes count 0.5 until the account is 3 days old and has a verified email (stops sockpuppets).
- **Ranking**: `wilson_score` (lower bound, 95%) for "Top", and `hot_score` = wilson × time-decay for "Trending". Both are recomputed when votes change.
- **Auto-hide**: an item is hidden when either condition holds:
  - weighted downvotes ≥ 8 and down-share ≥ 70%, or
  - ≥ 3 reports from distinct T1+ users.
  When that happens, status becomes `hidden` (the owner can still see it, with the reason), an audit entry is written, and a `cleanup_schedule` row is created for purge in 14 days unless a moderator restores it.
- **Trust levels** (automatic, nightly job): T0 → T1 after 7 days with no upheld reports; T1 → T2 after 3 published items with a positive Wilson score; T3 is manual (moderator). Mods and admins are Identity roles.
- **Moderator UI** (`/community/mod`):
  - Queues: reported, auto-hidden, flagged duplicates.
  - Actions: restore, remove now, schedule purge, ban user (temporary or permanent, which hides all their items), dismiss report.
  - Bulk actions and the audit log viewer.
- **Background jobs** (a hosted service with Postgres advisory-lock leader election): purge scheduled items, blob GC, download rollups, trust recompute, stock blob expiry.

### Auth (OpenIddict + Identity)
- Email + password with mandatory verification. Passkeys (ASP.NET Identity passkey support in .NET 10), TOTP MFA, password reset. Email goes through the existing Resend SMTP (`smtp.resend.com:465`).
- Social logins: GitHub, Google, Discord. Each turns on when its client id is configured.
- **Website**: cookie auth (same origin; the SPAs call `/api/v1` with the cookie and an antiforgery header).
- **Flows** (OpenIddict clients):
  - `drift-desktop`: authorization code + PKCE with a loopback or `cutwire://` redirect, plus the device authorization grant for headless use and Android TV. This gets the API ready for new Drift releases; the Drift changes themselves are out of scope.
  - `mcp`: OAuth 2.1 authorization code + PKCE with dynamic client registration and protected-resource metadata, as the MCP auth spec requires.
- Personal access tokens (scoped: `forge:read`, `forge:write`, `community:publish`) for MCP clients that can't do OAuth.
- Anonymous Drift and stock usage is identified by an `X-Studio-Install` id and IP rate limits. HMAC signing is dropped, because the key ships inside an open-source client and so isn't a secret.

### Forge integration and MCP fix
- Move `CutWire-Tinker/src/core` to `web/packages/forge-core` and `src/{features,services,state,shared}` to `web/apps/forge`. Base path `/forge`.
- **Cloud library**: when signed in, the IndexedDB library syncs with `forge_documents`. Writes use the revision for optimistic concurrency (on a conflict, a "keep both / take server" prompt appears). Base64 assets are uploaded as blobs and referenced by sha256, so documents stay small. Guests keep local-only storage and can "upload library" after signing in.
- **Live updates**: an SSE stream at `/api/v1/forge/documents/{id}/events` lets an open editor apply MCP or AI edits as they happen (each MCP batch becomes one undo step).
- **Shared logic, one source of truth**: `forge-core` is bundled into one ES2020 JS file (Vite lib mode), embedded in the backend, and run with **Jint** (a pure .NET JS engine, so the build stays single-binary). The MCP and AI tools (`src/core/ai/tools/{video,audio,shared}.ts`, the compiler and `check`) run there. The graph logic is not rewritten in C#.
- **MCP tools** (the official ModelContextProtocol C# SDK at `/mcp`):
  - Existing tools: `list_documents`, `open_document(id)`, `create_document(kind, name, starter?)`, `get_shader_code`, `check`, plus every existing BUILD_TOOLS tool, now on real document ids.
  - New tools: `import_package(base64 driftfx|zip|json)`, `export_driftfx(id)`, `publish_draft(id)` (creates a community draft; the human finishes the metadata and media).
  - Edits save to `forge_documents` with a revision bump and an SSE broadcast.
- **AI proxy**: port `server/ai.ts` (Workers AI `@cf/zai-org/glm-4.7-flash`, the daily neuron budget per user and globally, prompt caps). The agent loop runs in .NET and calls tools through Jint. Bring-your-own-key AI stays client-side as it is today.
- **Publish from Forge**: an Export dialog button, "Publish to Community". It builds the `.driftfx` client-side, captures the thumbnail at the exact required size from the WebGL preview (WebP encode, with a quality slider so it fits under the cap), and opens the upload form pre-filled.

### Stock (marketplace features brought back)
Port to .NET from `CutWire-Marketplace/backend/internal`:
- **Providers**: pexels, unsplash, pixabay, jamendo, freetouse, greenscreen, social, youtube/ytdlp, behind an `IStockProvider` interface.
- **Jobs**: resolve jobs, download jobs with ffmpeg normalisation, the quota ledger (per install / per account), sealed media URLs, the SFX library (bind-mounted `sfx.json` + FLAC/Opus), the Drift Assets mirror (GitHub release `index.json` + tarball).
- **Infrastructure kept**: imgproxy and potoken stay as sidecar containers.
- **Removed**: Redis, replaced by `IMemoryCache` plus a Postgres cache table for provider responses.
- **Web UI**: `/stock` in the shell, with card grids and carousels, re-implemented in React and based on the Marketplace views.

### Theme Maker (`/themes`)
- **Inputs**: `VideoEd/resources/themes/theme.schema.json`, `docs/theming-tokens.md` (generated into a JSON token catalog at build time from `dark.json` / `light.json`), and `extends` + `variants` semantics.
- **Editor**:
  - Start from dark, light, graphite or frost.
  - Edit by tier: palette → semantic → components (searchable, 563 groups), plus metrics, typography, motion and window.
  - `$ref` picker with cycle detection.
  - Correct `#AARRGGBB` handling (alpha first).
  - Diff view showing only the overrides compared with the base.
  - Contrast checker (WCAG) on key foreground/background pairs.
- **Live preview**: a React mock of the Drift main window (header, media bin, preview, timeline with clips, inspector, buttons, dialogs) driven by the resolved tokens. Light and dark toggle.
- **Output**: export `.json` / `.zip` (no QML). Publish as kind `theme`, which needs a 1280×720 screenshot; it can be auto-captured from the preview at exact size.
- Drafts sync to the account (`theme_drafts` table, or reuse the documents table with kind `theme`).

### API conventions
- `/api/v1`, resource-oriented. Cursor pagination. RFC 7807 errors with stable `code`s. ETags. OpenAPI 3.1 generated, and it drives `api-client`.
- Only additive changes inside v1. Separate `drift` client scopes, so the old marketplace contract is never involved.

## Deployment (`CutWire-Ansible`)
- New role `roles/studio` (copied from `roles/forge`):
  - compose with `postgres:17-alpine` (volume `pgdata`) and `ghcr.io/cutwire-studios/cutwire-studio`, on port **8087**, bound to 127.0.0.1, with `enable_ipv6: true`.
  - volume `blobs`, the read-only SFX bind mount, and imgproxy + potoken sidecars.
- `vars.yml`: `studio_*` vars. `http_sites` + `tunnel_ingress` for `cutwire.studio`. `nginx/templates/studio.conf.j2` (`client_max_body_size 16m`; pass-through `/files` caching headers). A `cloudflare_access_apps` entry for `/community/mod` is optional, as a second factor for moderators.
- `playbooks/studio.yml`, plus `.github/workflows/deploy-studio.yml` (repository_dispatch).
- Secrets via `bws-upsert`: DB password, OpenIddict signing/encryption certificates, SMTP, provider keys, CF AI token, social client secrets.
- Studio repo CI: `.github/workflows/build.yml` (web build + tests → dotnet test → image push to GHCR → dispatch deploy).
- Needs a Cloudflare zone for `cutwire.studio` (a manual step in Cloudflare; the `cloudflare` role then manages DNS and tunnel).

## Execution (subagents, sequential waves; I orchestrate and review)
- Each wave lists its agents and their scope. Agents within a wave touch separate directories.
- After each agent finishes, I review, run the build and tests, and **commit** (conventional, single line, no trailers, per your CLAUDE.md). Nothing is pushed.
- Model: `sonnet` for design-heavy work, `haiku` for well-defined mechanical tasks.

| Wave | Agents (model) | Deliverable / commit |
|---|---|---|
| 0 | me | `git init`, `.gitignore`, `README.md`, `docs/architecture.md` → `chore: scaffold repository` |
| 1 | A: backend skeleton, Clean Architecture, EF/Postgres, blob store, SPA hosting, OpenAPI (sonnet) · B: pnpm workspace, `@cutwire/ui` design system + tokens, app registry, shell launcher, `/edit` placeholder (sonnet) | `feat(backend): ...`, `feat(web): ...` |
| 2 | A: Identity + OpenIddict, email, passkeys/MFA, social logins, PATs, account endpoints (sonnet) · B: account UI in shell (sonnet) | `feat(auth): ...` |
| 3 | A: asset-kind handlers + `.driftfx`/theme/look validation, content gates, english dictionary, phash (sonnet) · B: items/versions/media/tags API, search (tsvector), downloads (sonnet) | `feat(community): ...` |
| 4 | A: votes, ranking, reports, trust levels, auto-hide, cleanup jobs, moderation API (sonnet) · B: community UI: browse/item/profile/upload with client-side gates, mod console (sonnet) | `feat(moderation): ...` |
| 5 | A: move Forge into `web/apps/forge` + `packages/forge-core`, base path, shared UI (sonnet) · B: forge documents API, sync, SSE, blob assets (sonnet) | `feat(forge): ...` |
| 6 | A: Jint forge-core runtime, MCP server, OAuth for MCP, AI proxy port (sonnet) · B: Forge cloud library UI, conflict handling, Publish-to-Community flow (sonnet) | `feat(mcp): ...` |
| 7 | A: Theme Maker app (sonnet) · B: token catalog generator script from VideoEd themes (haiku) | `feat(themes): ...` |
| 8 | A: stock providers, jobs, quota, SFX, Drift Assets port (sonnet) · B: `/stock` UI (sonnet) | `feat(stock): ...` |
| 9 | A: Dockerfile, single-file publish, CI workflow (haiku) · B: Ansible `studio` role, nginx, playbook, deploy workflow, in CutWire-Ansible (sonnet; changes left **uncommitted** there for your review) | `build: ...` |

Each agent prompt contains: the scope (directories it owns), the relevant sections of this plan, the source files to port from (paths above), the acceptance criteria (builds, tests pass), and a ban on touching other areas or committing.

## Prerequisites (please install before I start; I won't work around them)
- `.NET 10 SDK` (`dotnet` is not installed)
- `podman` (or docker), for local Postgres and Testcontainers integration tests
- Optional: `postgresql` client (`psql`) for debugging

## Verification
- `pnpm -r build && pnpm -r test` (includes the moved Forge vitest suite and `check-shaders`).
- `dotnet build && dotnet test` (unit tests for gates, ranking and the `.driftfx` parser, using fixtures from `CutWire-Tinker/scripts/make-drift-fixtures.ts`; Api integration tests against Testcontainers Postgres: upload → gate rejects/accepts → vote → auto-hide → purge job).
- MCP: run the binary locally and connect with `npx @modelcontextprotocol/inspector`. Confirm `list_documents` shows a document created in the browser, and that an edit appears live in the open editor.
- `podman build -f deploy/Dockerfile` produces the image; `ansible-playbook --check playbooks/studio.yml`.
- Per your instructions: no visual or screenshot verification. Building is the bar.

## Out of scope / follow-ups
- Drift client changes to use the new API (the old marketplace stays live meanwhile).
- Drift Web itself (only `/edit` is reserved, with COOP/COEP).
- Language packs: Drift can't load translations at runtime yet (embedded `.qm` only). The kind is defined in the registry but disabled.
- Offsite backups: there are no backups on edge1 today. Recommend nightly `pg_dump` + restic of `blobs`; the target still needs to be decided.
