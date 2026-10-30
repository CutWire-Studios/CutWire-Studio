# Auth and accounts

Contract for the SPAs (and OAuth clients) of the CutWire Studio backend. One origin: the SPAs live under `PublicUrl`, the API under `/api/v1`, OAuth under `/connect`.

## Session model

- Website session: cookie `__Host-studio` (`studio` in Development), HttpOnly, SameSite=Lax, Secure (not in Development), sliding 30 days. Sign-in persists it for 30 days by default.
- Every non-GET request to `/api/v1/**` (and `POST /connect/verify`) without an `Authorization` header must send `X-Requested-With` (any value). If `Origin` is present it must equal the origin of `PublicUrl` (or an entry of `Auth:AllowedOrigins`). Failures are 403 problems with `code` `csrf_header_required` or `csrf_origin`.
- Unauthenticated API calls return `401` problem JSON (never a redirect). Banned users get `403` with `code: banned` on endpoints using the `not-banned` policy.
- Bearer callers: `Authorization: Bearer <OpenIddict JWT>` or `Bearer cwpat_...` (personal access token). Both yield a principal with `sub` and scopes.
- Account management endpoints below marked **session** accept the cookie only.

Errors are RFC 7807 problems with a stable `code` and, for validation, `errors: { field: [messages] }`. Content-rule failures also carry `violations: [{field, code, message, params}]` (see `../../docs/quality-gates.md`); auth validation problems only have `errors`.

## Sign-in flow

`POST /api/v1/account/sign-in {email, password, rememberMe?}` returns `{status}`:

| status | meaning | next |
|---|---|---|
| `ok` | cookie issued | go to `return` target |
| `requires_verification` | password right, email not verified (no cookie) | show "check your email", offer resend |
| `requires_2fa` | password accepted, second step pending | `POST /account/sign-in-2fa {code}` or `{recoveryCode}` |
| `pending_profile` | external login has no profile yet (see External) | `/account/pending-profile` |

Errors: `401 auth.invalid_credentials`, `403 auth.locked_out` (5 failures lock the account for 10 minutes).

## Endpoints (`/api/v1`)

Anonymous (strict per-IP rate limit where marked *):
- `POST /account/sign-up` * `{email, password, handle, displayName?}` -> 202 `{status: "verification_sent"}`. Duplicate email is silent; duplicate handle is `409 profile.handle_taken`. Password min length 10.
- `POST /account/sign-in` *, `POST /account/sign-in-2fa` * `{code? | recoveryCode?, rememberMe?, rememberMachine?}` -> `{status:"ok"}`
- `POST /account/sign-out` -> 204
- `POST /account/verify-email {userId, token}` * -> 204 (email does not sign the user in)
- `POST /account/resend-verification {email}` * -> 202
- `POST /account/forgot-password {email}` * -> 202 (always); `POST /account/reset-password {email, token, newPassword}` * -> 204
- `POST /account/confirm-email-change {userId, email, token}` * -> 204
- `POST /account/passkeys/request-options` * -> WebAuthn request options JSON; `POST /account/passkeys/sign-in {credential}` * -> `{status:"ok"}`

Email links point at SPA routes: `/account/verify-email?userId&token`, `/account/reset-password?email&token`, `/account/confirm-email-change?userId&email&token`. Tokens are opaque base64url strings; pass them back unchanged.

Session (**session**):
- `GET /me` (also bearer) -> `{id, handle, displayName, avatarUrl, email, emailVerified, roles, trustLevel: "T0".."T3", banned}`; 401 when signed out.
- `POST /account/change-password {currentPassword, newPassword}` (accounts without a password omit `currentPassword`), `POST /account/change-email {newEmail, password}` -> 202 (link goes to the new address), `POST /account/sessions/sign-out-everywhere` (keeps the current session), `POST /account/delete {password}` (anonymizes the profile to "Deleted user", revokes tokens, frees the email).
- Profile: `GET|PUT /account/profile {handle, displayName, bio}`, `POST /account/profile/avatar` (raw image body, WebP/PNG/JPEG, at most 256 KB and 512x512), `DELETE /account/profile/avatar`. Handles are 3-24 of `a-z0-9_-`, case-insensitive (stored lowercase). Avatars are served from `/files/{sha256}`.
- 2FA: `GET /account/2fa` `{enabled, hasAuthenticator, recoveryCodesLeft}`, `POST /account/2fa/setup` `{sharedKey, otpAuthUri}`, `POST /account/2fa/enable {code}` -> `{recoveryCodes[10]}`, `POST /account/2fa/disable {password}`, `POST /account/2fa/recovery-codes {password}` (regenerate).
- Passkeys: `POST /account/passkeys/creation-options` -> WebAuthn creation options JSON; `POST /account/passkeys {credential, name?}` (`credential` is the JSON of `navigator.credentials.create()`); `GET /account/passkeys`; `PATCH /account/passkeys/{id} {name}`; `DELETE /account/passkeys/{id}`.
- Personal access tokens: `GET /account/tokens`, `POST /account/tokens {name, scopes, expiresAt?}` -> `{token, secret}` (secret shown once; default expiry 90 days, max 1 year; scopes from `forge:read forge:write community:publish`), `DELETE /account/tokens/{id}`.
- Connected apps (OAuth grants): `GET /account/connected-apps`, `DELETE /account/connected-apps/{id}` (revokes the grant and its tokens).
- Consent: `GET /account/consent-info?return=<authorize url>` -> `{clientId, clientName, scopes[{name, displayName, description}], alreadyGranted}`, `POST /account/consent {return, approve}` -> `{redirect}`; the SPA navigates to `redirect` (the authorize URL when approved, the client `redirect_uri` with `error=access_denied` when denied). Consent is remembered per client and scopes.

External logins (GitHub, Google, Discord; each only when `Auth:External:<Provider>:ClientId` is set):
- `GET /account/external` -> `[{name, displayName}]`
- `GET /account/external/{provider}?return=/path` -> redirect to the provider. Register `${PublicUrl}/signin-github`, `/signin-google`, `/signin-discord` as the provider callback URLs.
- Back in the app (`GET /account/external/callback`) the user is redirected to:
  - `return` (default `/`) when signed in (existing link, or an existing account with the same verified email was linked);
  - `/account/pending-profile?return=...` for a new user: call `GET /account/external/pending` -> `{provider, email, suggestedHandle, suggestedDisplayName}` and finish with `POST /account/external/complete {handle, displayName?}` -> `{status:"ok"}`;
  - `/account/sign-in?error=<code>&return=...` with `external_email_unverified` (provider did not verify the email), `external_email_conflict` (an unverified local account owns the email), `locked_out`, or `external_failed`.
- External sign-ins bypass TOTP (the provider is the second factor).

## OAuth / OpenID Connect

Issuer is `PublicUrl` (note the trailing slash in metadata). Discovery: `/.well-known/openid-configuration` and `/.well-known/oauth-authorization-server`; keys: `/.well-known/jwks`.

Endpoints: `/connect/authorize`, `/connect/token`, `/connect/device`, `/connect/verify`, `/connect/userinfo`, `/connect/revocation`, `/connect/introspection`, `/connect/end-session`, `/connect/register`.

- Flows: authorization code with PKCE (S256, required), refresh tokens (rotating, 30 days), device authorization. Access tokens are signed JWTs valid for 1 hour (not encrypted, `sub` and `scope` claims, `aud` from the `resource` parameter or the scopes' resources).
- Scopes: `openid profile email offline_access forge:read forge:write community:publish drift`. Resource indicators (RFC 8707): `PublicUrl/` and `PublicUrl/mcp`.
- Seeded public clients: `drift-desktop` (redirects `http://127.0.0.1/callback` with any port, `cutwire://auth/callback`; device grant; all scopes) and `mcp` (placeholder). Loopback redirect URIs (`127.0.0.1`, `[::1]`, `localhost`) registered over `http` match on host, path and query with any port.
- Authorize: if the browser has no session the endpoint redirects to `/account/sign-in?return=<authorize url>`; without a remembered grant it redirects to `/account/consent?return=<authorize url>`. After sign-in or consent the SPA sends the browser back to `return`. Errors that cannot be redirected (bad client, redirect URI, PKCE) are returned as a 400 text body `error:...`.
- Device flow: the client calls `POST /connect/device`, shows `verification_uri` (`/connect/verify`) and `user_code`. `GET /connect/verify?user_code=` redirects to `/account/sign-in?return=...` when signed out, `/account/device?error=invalid_code` for unknown codes, otherwise `/account/device?user_code=&client=&scope=` for the SPA approve screen (`/account/device` without a code is the code entry form, it should navigate to `/connect/verify?user_code=`). The SPA approves with `POST /connect/verify` (form fields `user_code`, `submit=accept|deny`, header `X-Requested-With`, session cookie); the response is a 302 to `/account/device?result=approved|denied`.
- Dynamic client registration (RFC 7591): `POST /connect/register` with JSON `{client_name, redirect_uris, grant_types?, response_types?, token_endpoint_auth_method?: "none", scope?}` -> 201 `{client_id, client_id_issued_at, ...}`. Public PKCE clients only; redirect URIs must be `https` or loopback `http` (at most 5), scopes `openid profile email offline_access forge:read forge:write community:publish`. Errors use the OAuth shape `{error, error_description}` (400). Rate limited per IP. Registered clients that never received a grant or token are deleted after 7 days.
- Protected resource metadata (RFC 9728): `/.well-known/oauth-protected-resource` and `/.well-known/oauth-protected-resource/mcp` -> `{resource: PublicUrl/mcp, authorization_servers, scopes_supported, bearer_methods_supported}`.

## Authorization policies (for endpoint authors)

`AuthPolicies.Scope("forge:read")` (`scope:forge:read`), `AuthPolicies.Role("moderator")` (`role:moderator`, cookie sessions only: bearer tokens carry no roles), `AuthPolicies.NotBanned` (`not-banned`), `AuthPolicies.Session`, `AuthPolicies.AdminArea` / `AuthPolicies.AdminOnly` (the `/admin` Razor Pages: Identity cookie scheme only, moderator or admin / admin, plus not-banned; unauthenticated requests redirect to `/account/sign-in?return=`). Cookie sessions satisfy every `scope:` policy. Test code can add endpoints through `IApiContributor`.

## Configuration

| key | purpose |
|---|---|
| `PublicUrl` | origin used in emails, issuer, CSRF check, passkey RP id (host) |
| `Auth:BootstrapAdminEmail` | verified account with this email gets the `admin` role on startup, verification or sign-in |
| `Auth:AllowedOrigins` | extra allowed `Origin` values (dev servers) |
| `Auth:DataProtectionKeysPath` | persist cookie/antiforgery keys (required when running more than one instance or when keys must survive restarts) |
| `Auth:OpenIddict:SigningCertificatePath/Password`, `EncryptionCertificatePath/Password` | PFX files; required outside Development. `UseEphemeralKeys=true` is for tests only |
| `Auth:External:{GitHub,Google,Discord}:ClientId/ClientSecret` | enable a provider |
| `Email:Smtp:Host/Port/User/Password/From` | SMTP (MailKit). Port 465 uses implicit TLS, others STARTTLS. Without `Host`, mails are logged and written to `Email:DevDirectory` (`data/mail/*.eml`) |
| `RateLimiting:StrictPermitLimit/StrictWindow` | per-IP limit for sign-in, sign-up, password reset, verification, passkey sign-in, DCR (default 10 per 5 minutes) |

Production values: `Email__Smtp__Host=smtp.resend.com`, `Port=465`, `User=resend`, `Password=<api key>`, `From=no-reply@cutwire.studio`.
