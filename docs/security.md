# Security

Result of a security review and hardening pass. Code: `backend/src/CutWire.Studio.Api/Http/SecurityHeaders.cs` (headers, CSP report endpoint), tests in `SecurityHeadersTests.cs` (integration) and `SecurityTests.cs` (unit), build check in `web/scripts/check-dist.mjs`.

## Threat model summary

One origin serves the SPAs, the JSON API, the OIDC provider and user-supplied bytes, so anything that runs script on that origin owns the session. The order of concern:

| Asset | Threats | Main controls |
| --- | --- | --- |
| Sessions, PATs, OAuth grants | XSS, CSRF, open redirect, brute force, token theft by a client with a malicious redirect URI | Strict CSP with no inline script, `__Host-` HttpOnly Secure SameSite=Lax cookie, `X-Requested-With` plus `Origin` check on every cookie-authenticated mutation, no CORS, PKCE S256 required, exact (or loopback-port) redirect URI match, rate limits, scopes on PATs/JWTs, roles only on cookie sessions |
| User content (items, profiles, Forge docs, theme drafts) | Stored XSS through descriptions, filenames, SVG/HTML served from `/files`, IDOR | React text rendering only (no `dangerouslySetInnerHTML`), http(s)-only linkify, blob mime allow-list at upload, `/files` sandboxed + forced download for non-passive types, owner filter on every query |
| Server resources | SSRF from stock/Drift Assets, command injection through yt-dlp/ffmpeg, zip/tar bombs, image bombs, regex DoS, request floods | See the findings table |
| Provider API keys, DB | Leaks through logs, cache tables, committed config | No secrets in the repo, provider cache keys hashed, problem+json without stack traces in production |

Trust boundaries: browsers and API clients are untrusted; stock providers, yt-dlp output and the Drift Assets GitHub release are untrusted input that is validated before use; the forge-core bundle running in Jint is trusted code processing untrusted data (no CLR interop is exposed, with time, memory and recursion limits).

## Headers and CSP per route class

Configured under `SecurityHeaders` (appsettings or `SecurityHeaders__...` env vars). Defaults live in code so a deployment with only environment variables still gets the full policy. `CspReportOnly=true` swaps the header name for every class; `ReportUri` empty disables reporting. Per-app overrides use `SecurityHeaders:Apps:<dir>:Csp:<directive>` where `<dir>` is the `AppHost.Dir` (`shell`, `forge`, `themes`, `edit`); the value `~` removes a directive.

Note on report-only: browsers ignore `frame-ancestors` and `sandbox` in a report-only policy, so `X-Frame-Options: DENY` is sent on SPA and API responses as well.

### SPA responses (every path outside the reserved prefixes, so HTML and assets of the four apps)

```
default-src 'self'; script-src 'self'; style-src 'self'; style-src-elem 'self' 'unsafe-inline';
img-src 'self' data: blob: [imgproxy origin]; media-src 'self' blob: [imgproxy origin];
connect-src 'self'; font-src 'self'; worker-src 'self'; manifest-src 'self'; frame-src 'none';
frame-ancestors 'none'; base-uri 'none'; form-action 'self'; object-src 'none';
upgrade-insecure-requests; report-uri /api/v1/csp-report; report-to csp-endpoint
```

Reasoning and relaxations:

- `script-src 'self'`, no `unsafe-inline`/`unsafe-eval`. The theme-flash scripts were inline in all four `index.html`; they are now `public/theme-init.js` per app (loaded synchronously in `<head>`, so no flash). `pnpm check:dist` fails the build if an `index.html` gets an inline script/style/handler or a bundle gains `eval(`/`new Function(`.
- The Theme Maker used Ajv's runtime compiler (`new Function`). The schema is now compiled at development time (`pnpm --filter @cutwire/drift-theme gen:validator`, output committed, also run by `sync`), so no `unsafe-eval` is needed. The only remaining `Function("")` is zod's JIT feature probe in Forge, inside try/catch, which falls back to the interpreter when blocked (it shows up as one report-only violation, not a failure).
- `style-src-elem 'unsafe-inline'` is the one real relaxation. Radix (`react-remove-scroll-bar`) and Sonner append `<style>` elements at runtime and Sonner cannot take a nonce. Inline style *attributes* stay blocked (`style-src 'self'` applies to `style-src-attr`): React applies `style` props through the CSSOM, which CSP does not govern. Only styles can be injected this way, never script. If a violation report shows a real need for `style-src-attr`, add it for that app only.
- Forge only: `script-src 'self' 'wasm-unsafe-eval'` (zstd and the audio engine are WebAssembly; the AudioWorklet loads under `script-src`) and `connect-src 'self' https: http://localhost:* http://127.0.0.1:* http://[::1]:*`. Forge supports bring-your-own AI keys against Anthropic, OpenAI or a user-chosen OpenAI-compatible server (often local), so the destination cannot be enumerated. Trade-off: script injection is already blocked, so the wider `connect-src` mostly matters as an exfiltration route; tighten it to the two provider hosts if the custom-server option is dropped. Forge also keeps `microphone=(self)` in Permissions-Policy (audio preview input).
- `img-src`/`media-src` include `data:` (QR code), `blob:` (Forge and theme screenshots), and the imgproxy origin read from `Stock:Media:Imgproxy:Prefix`. Stock providers' own hosts are never used by the browser: previews and thumbnails go through the sealed relay.
- `worker-src 'self'`: no `new Worker` exists in any bundle (checked in `dist`), so `blob:` is not allowed.
- `upgrade-insecure-requests` is off in Development (`UpgradeInsecureRequests` unset means "on outside Development"); HSTS is sent on https requests outside Development (1 year, includeSubDomains, preload off).
- `require-trusted-types-for 'script'` is **not** enabled. React DOM writes `innerHTML` internally (script element creation, SVG), and several libraries do the same, so enforcing it would break the apps without a default policy that would gain nothing. Revisit when React ships Trusted Types support.
- `form-action 'self'` is enough for the auth flows: the SPA never submits a native form to `/connect/*`. Sign-in, consent and device approval are `fetch` calls (`POST /account/consent`, `POST /connect/verify`), and the final hop to `/connect/authorize` and on to the client's `redirect_uri` (loopback or `cutwire://`) is a top-level navigation (`location.assign` plus 302), which `form-action` does not govern. No redirect-URI allow-listing in `form-action` is therefore needed. If a server-rendered consent form is ever added, add the registered redirect origins to `form-action` for `/account/consent` through `SecurityHeaders:Apps:shell`.

### API: `/api`, `/connect`, `/mcp`, `/.well-known`

`default-src 'none'; frame-ancestors 'none'` plus the report directives. `Cross-Origin-Resource-Policy: same-origin`. There is no CORS configuration, so browsers cannot read these responses cross-origin; native clients (Drift) are unaffected.

### Byte routes: `/files/*`, `/api/v1/stock/files/*`, `/api/v1/stock/media/*`, `/api/v1/stock/sfx/files/*`

`default-src 'none'; sandbox`, `X-Content-Type-Options: nosniff`, and on 200/206 responses a forced `Content-Disposition: attachment` unless the type is `image/*` (not SVG), `audio/*` or `video/*`. An existing `inline; filename=` is rewritten to `attachment; filename=`. These routes carry user uploads and third-party bytes, so even a browser that is navigated to one cannot run it as a document.

CORP: `/files/{sha256}` is `cross-origin`. It is public, content-addressed and immutable (avatars, thumbnails, packages), the URL is a capability the owner already shares, and third-party pages or a future separate-origin Drift Web should be able to embed it; nothing private is served there. Every other class is `same-origin` (sealed stock URLs are credentials, API responses are never meant for other sites).

### Everything else

`X-Content-Type-Options: nosniff`, `Referrer-Policy: strict-origin-when-cross-origin` (email links carry one-time tokens in the query string), `Cross-Origin-Opener-Policy: same-origin`, `Permissions-Policy` denying accelerometer, ambient-light-sensor, bluetooth, camera, display-capture, geolocation, gyroscope, hid, magnetometer, microphone, midi, payment, serial, usb and xr-spatial-tracking (passkeys and clipboard-write keep their defaults). `/edit` keeps `Cross-Origin-Embedder-Policy: require-corp` from its `AppHost`; the middleware never overwrites a header an app host already set. `Server` header is removed.

### Reporting

`POST /api/v1/csp-report` (hidden from OpenAPI) accepts `application/csp-report` and `application/reports+json`, is exempt from the `X-Requested-With` check (browsers cannot send it), is limited to 16 KB and 30 requests per IP per minute, and logs at Warning with bounded, control-character-free fields. `Reporting-Endpoints` is sent for the Reporting API. Suggested rollout: deploy with `SecurityHeaders__CspReportOnly=true`, watch the log for `CSP violation`, then switch to enforcing. Browser behaviour was not exercised here (no visual verification): the policy is derived from the built bundles, so run report-only first.

## Findings

Severity is the practical impact in this deployment. "Fixed" items have regression tests unless noted.

| # | Severity | Location | Finding | Status |
| --- | --- | --- | --- | --- |
| 1 | High | `Application/Quality/TextRules.cs`, `TextGate.cs`, `TitleGate.cs` | The link-detection regex backtracks quadratically on long dotted input (`a.a.a.a...`) and ran on unbounded title/summary/description (requests up to the body limit): a single authenticated request pins a CPU for minutes (reproduced with a 200 KB input). | Fixed: length checks first, 250 ms regex timeout treated as "has links"; unit tests |
| 2 | Medium | `Api/Http/SecurityHeaders.cs`, `FileEndpoints.cs`, `DriftAssetsMirror.cs` | `/files/{sha}` served stored blobs inline on the main origin with the stored type. The Drift Assets mirror stores `image/svg+xml` and `.gltf`, so a compromised upstream release (or any future upload path) would be same-origin stored XSS. Upload paths already restrict types (no SVG/HTML). | Fixed: sandbox CSP, nosniff, forced attachment for non-passive types; integration tests |
| 3 | Medium | `Api/Endpoints/StockEndpoints.cs` (`/stock/media/{token}`) | The preview relay echoed the upstream `Content-Type`, so a provider thumbnail URL returning HTML would be served from our origin. | Fixed: non-media types refused (502) and the byte-route headers apply |
| 4 | Medium | `Application/Stock/StockAssembler.cs`, `web/apps/shell/src/stock/item.tsx` | `creator.url` and `license.url` come from providers/yt-dlp and became `<a href>` unchanged (`javascript:` XSS vector). | Fixed: only http(s) URLs pass the assembler; unit test |
| 5 | Medium | `Api/Mcp/McpSetup.cs` | A banned user could keep using the MCP endpoint (create/import documents, create community drafts through `publish_draft`) with a PAT; only REST routes enforced `not-banned`. | Fixed: `NotBanned` requirement on the MCP policy; integration test |
| 6 | Medium | `Api/Endpoints/ExternalLoginEndpoints.cs` via `ResultExtensions.SafeLocalPath` | Open redirect after social login: `return=/%09/evil.example` passed the check, and browsers strip the tab, giving `//evil.example`. | Fixed: control characters and backslashes rejected; unit tests |
| 7 | Medium | `Infrastructure/Quality/SkiaImageAnalyzer.cs` | The gate decoded an uploaded WebP/PNG at the size its header claimed before checking dimensions: a few KB can force a multi-GB allocation. | Fixed: refused above 16.7 M pixels before decoding; unit test with a crafted 8000x8000 PNG |
| 8 | Medium | `Infrastructure/Oidc` device flow, `Api/Program.cs` | `/connect/device` and `/connect/verify` are answered by OpenIddict before the rate limiter middleware, so user codes could be guessed without limit. | Fixed: early per-IP limiter (20/min, `RateLimiting:DeviceFlowPermitLimit`); integration test |
| 9 | Low | `Infrastructure/Stock/ProviderCache.cs` | Provider responses were cached by full URL; Pixabay's API key is in the query string, so the key was stored in plaintext in `provider_cache.key`. | Fixed: keys are SHA-256 digests (old rows expire with their TTL); unit test |
| 10 | Low | `Api/Program.cs`, `HttpExtensions.cs` | Kestrel allowed 30 MB bodies and multipart up to 128 MB although the largest legitimate upload is 8 MB; `Server` header disclosed Kestrel. | Fixed: 16 MB limit (matches nginx), server header removed |
| 11 | Low | `Directory.Packages.props` | `System.Security.Cryptography.Xml` 10.0.11 (3 high advisories, GHSA-7hqp-7c5m-4vq6, GHSA-c399-9853-6jmj, GHSA-r955-8q5j-3h83) pulled in transitively through OpenIddict's DataProtection packages. Not reachable (no XML encryption configured) and absent from the `Api` project's own report. | Fixed: transitive pin to 10.0.13; `dotnet list package --vulnerable` is clean |
| 12 | Low | `web/packages/drift-theme` | Ajv `new Function` at runtime would have forced `unsafe-eval` on `/themes`. | Fixed: standalone validator generated at dev time; test |
| 13 | Info | CSP | Inline theme scripts in four `index.html`. | Fixed: external `theme-init.js` |
| 14 | Low | `Application/Stock` quota, `Api/Endpoints/StockEndpoints.cs` | Anonymous stock quota and per-client job caps are keyed by the client-supplied `X-Studio-Install` header, so rotating the id resets them. Mitigated by the per-IP `stock-write` limiter (30/min) and global caps; the design deliberately avoids per-IP quota because of NAT. | Open: needs a product decision (add an IP-keyed ceiling next to the install id) |
| 15 | Low | `Infrastructure/Oidc/DynamicClientRegistration.cs`, consent UI | Open dynamic client registration lets anyone register a client whose name is shown on the consent screen (impersonation/phishing). Redirect URIs must be https or loopback and consent is explicit. | Open: consider showing the redirect host on the consent screen and moderating names |
| 16 | Low | `Api/appsettings.json`, `Http/HttpExtensions.cs` | `AllowedHosts` is `*` and forwarded headers trust only loopback proxies. URLs in email use `PublicUrl`, so there is no host-header injection today; set `AllowedHosts` and the trusted proxy networks in the deployment. | Open: deployment configuration |
| 17 | Low | `deploy/Dockerfile` | yt-dlp and the bgutil plugin are downloaded by version without a checksum check. | Open: pin and verify SHA-256 values once chosen |
| 18 | Info | `appsettings.json` | Dev database password `studio` is the default connection string. Production must supply `ConnectionStrings__Default`; no other secret is committed (checked all tracked files). | Documented |
| 19 | Info | Forge | BYO AI keys live in the browser, which makes the strict CSP the main protection for them. | Documented |

### Reviewed and dismissed (false positives or already safe)

- **SQL injection:** the only raw SQL is `ExecuteSqlAsync`/`SqlQuery` with interpolated *parameters* (FormattableString), plus fixed text for advisory locks. Search uses `WebSearchToTsQuery`/trigram functions; sort is a whitelist (`top|trending|new|downloads`); cursors are typed.
- **Command injection:** yt-dlp and ffmpeg are started with `ProcessStartInfo.ArgumentList` (no shell). yt-dlp targets are http(s) URLs or `ytsearchN:` strings built in-process, reject a leading `-`, and follow `--`; YouTube ids are regex-validated; social URLs must match an exact host allow-list. ffmpeg inputs are absolute workspace paths verified not to escape or be symlinks, with `-protocol_whitelist file,crypto`, so a leading `-` is impossible. yt-dlp's reported output path is re-validated.
- **SSRF:** all outbound media fetches (downloads, relay, Drift Assets) use a client that resolves DNS itself and refuses loopback/private/link-local/CGNAT addresses on every connection (redirects included, no proxy). Provider REST calls go to fixed hosts with escaped ids. yt-dlp is only handed owned hosts. The AI proxy posts to a configured Cloudflare endpoint. Residual: `IsPublic` does not special-case NAT64/6to4 prefixes, which do not route to internal hosts on a normal deployment.
- **Path traversal / archives:** the blob store is content-addressed by a parsed SHA-256; SFX paths pass `SafeRel` plus a full-path prefix check and symlink refusal; the Drift Assets tar is never extracted by entry name (files are matched to the index and written under hashed temp names, with compressed and expanded size caps); theme zips whitelist three entry names with entry/total/count caps and bounded reads; `.driftfx` and share links check declared sizes before allocating.
- **CSRF / CORS:** every non-GET under `/api/v1` without `Authorization` needs `X-Requested-With` and a same-origin `Origin`; cookies are `SameSite=Lax`; no CORS policy exists. `/mcp` takes bearer tokens only.
- **Authz / IDOR:** Forge documents, theme drafts, items (`LoadOwnedAsync`), download jobs, PATs and connected apps are all filtered by owner id in the query; moderation routes need the `moderator`/`admin` role, which bearer tokens never carry; PAT scopes are enforced by `scope:` policies; mass assignment is not possible (explicit request records).
- **OpenIddict:** PKCE S256 required globally and per client, no implicit/password grants, exact redirect URI matching (loopback ports only for loopback `http`), dynamic clients limited to https/loopback and non-secret scopes, signing/encryption certificates required outside Development, refresh tokens rotate.
- **Cookies:** `__Host-studio`, HttpOnly, Secure, SameSite=Lax, path `/`.
- **Errors:** `UseExceptionHandler` with problem details does not include exceptions outside Development (tested with a throwing endpoint).
- **Jint:** engine is created without `AllowClr`, runs only the embedded forge-core bundle with timeout, memory and recursion limits; user data enters as strings. Residual: pooled engines are reused across users, so a prototype-pollution bug in forge-core would cross users; forge-core is first-party and does not merge untrusted objects into prototypes.
- **XSS in the SPAs:** no `dangerouslySetInnerHTML`, `innerHTML` or `document.write` in app code; descriptions and changelogs render as text with an http(s)-only linkifier (`rel="noopener noreferrer nofollow ugc"`); `Content-Disposition` names are sanitised server-side; JSON is never embedded in HTML.
- **Dependencies:** `pnpm audit` reports no known vulnerabilities.
