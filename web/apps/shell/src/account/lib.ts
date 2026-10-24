import type { ApiError, SignInStatus } from "@cutwire/api-client"

const BASE = "https://same-origin.invalid"

/** Returns a same-origin path (with query and hash) or the fallback. Rejects protocol-relative, backslash and cross-origin targets. */
export function sanitizeReturn(raw: string | null | undefined, fallback = "/", origin: string = globalThis.location?.origin ?? BASE): string {
  if (!raw) return fallback
  if (raw.includes("\\") || [...raw].some((c) => c.charCodeAt(0) < 32)) return fallback
  if (raw.startsWith("//")) return fallback
  if (!raw.startsWith("/") && !/^https?:\/\//i.test(raw)) return fallback
  try {
    const url = new URL(raw, origin)
    if (url.origin !== origin) return fallback
    return url.pathname + url.search + url.hash
  } catch {
    return fallback
  }
}

export function withReturn(path: string, ret: string): string {
  return ret && ret !== "/" ? `${path}?return=${encodeURIComponent(ret)}` : path
}

export type SignInOutcome =
  | { kind: "done"; to: string }
  | { kind: "two-factor" }
  | { kind: "verify" }
  | { kind: "pending-profile"; to: string }
  | { kind: "unknown"; status: string }

export function signInOutcome(status: SignInStatus | string, ret: string): SignInOutcome {
  switch (status) {
    case "ok":
      return { kind: "done", to: ret }
    case "requires_2fa":
      return { kind: "two-factor" }
    case "requires_verification":
      return { kind: "verify" }
    case "pending_profile":
      return { kind: "pending-profile", to: withReturn("/account/pending-profile", ret) }
    default:
      return { kind: "unknown", status }
  }
}

export function toBase64Url(buf: ArrayBuffer | Uint8Array): string {
  const bytes = buf instanceof Uint8Array ? buf : new Uint8Array(buf)
  let bin = ""
  for (const b of bytes) bin += String.fromCharCode(b)
  return btoa(bin).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "")
}

export function fromBase64Url(s: string): Uint8Array<ArrayBuffer> {
  const b64 = s.replace(/-/g, "+").replace(/_/g, "/").padEnd(Math.ceil(s.length / 4) * 4, "=")
  const bin = atob(b64)
  const out = new Uint8Array(bin.length)
  for (let i = 0; i < bin.length; i++) out[i] = bin.charCodeAt(i)
  return out
}

interface Descriptor {
  id: string
  [k: string]: unknown
}

type Json = Record<string, any>

const toDescriptors = (list: Descriptor[] | undefined) => list?.map((d) => ({ ...d, id: fromBase64Url(d.id) }))

/** Converts server WebAuthn options JSON (base64url binary fields) to the shape navigator.credentials expects. */
export function toPublicKeyOptions(json: Json): Json {
  const o: Json = { ...(json.publicKey ?? json) }
  o.challenge = fromBase64Url(o.challenge)
  if (o.user) o.user = { ...o.user, id: fromBase64Url(o.user.id) }
  if (o.allowCredentials) o.allowCredentials = toDescriptors(o.allowCredentials)
  if (o.excludeCredentials) o.excludeCredentials = toDescriptors(o.excludeCredentials)
  return o
}

/** Serialises a PublicKeyCredential (assertion or attestation) to the JSON the server expects. */
export function credentialToJson(cred: PublicKeyCredential): Json {
  const r = cred.response as AuthenticatorAssertionResponse & AuthenticatorAttestationResponse
  const response: Json = { clientDataJSON: toBase64Url(r.clientDataJSON) }
  if (r.attestationObject) {
    response.attestationObject = toBase64Url(r.attestationObject)
    if (typeof r.getTransports === "function") response.transports = r.getTransports()
  } else {
    response.authenticatorData = toBase64Url(r.authenticatorData)
    response.signature = toBase64Url(r.signature)
    if (r.userHandle) response.userHandle = toBase64Url(r.userHandle)
  }
  return {
    id: cred.id,
    rawId: toBase64Url(cred.rawId),
    type: cred.type,
    authenticatorAttachment: cred.authenticatorAttachment ?? undefined,
    clientExtensionResults: cred.getClientExtensionResults(),
    response,
  }
}

export const passkeysSupported = () => typeof window !== "undefined" && "PublicKeyCredential" in window

export async function getPasskeyAssertion(options: Json): Promise<Json> {
  const cred = (await navigator.credentials.get({ publicKey: toPublicKeyOptions(options) as PublicKeyCredentialRequestOptions })) as PublicKeyCredential | null
  if (!cred) throw new Error("Passkey sign-in was cancelled.")
  return credentialToJson(cred)
}

export async function createPasskeyCredential(options: Json): Promise<Json> {
  const cred = (await navigator.credentials.create({ publicKey: toPublicKeyOptions(options) as PublicKeyCredentialCreationOptions })) as PublicKeyCredential | null
  if (!cred) throw new Error("Passkey creation was cancelled.")
  return credentialToJson(cred)
}

export const HANDLE_RE = /^[a-z0-9_-]{3,24}$/

export function validateHandle(handle: string): string | null {
  const h = handle.trim().toLowerCase()
  if (h.length < 3) return "Use at least 3 characters."
  if (h.length > 24) return "Use at most 24 characters."
  if (!HANDLE_RE.test(h)) return "Only letters, numbers, underscore and hyphen are allowed."
  return null
}

export const PASSWORD_MIN = 10

export function passwordStrength(pw: string): { score: 0 | 1 | 2 | 3 | 4; label: string } {
  if (!pw) return { score: 0, label: "" }
  let score = 0
  if (pw.length >= PASSWORD_MIN) score++
  if (pw.length >= 14) score++
  if (/[a-z]/.test(pw) && /[A-Z]/.test(pw)) score++
  if (/\d/.test(pw) && /[^A-Za-z0-9]/.test(pw)) score++
  if (pw.length < PASSWORD_MIN) score = Math.min(score, 1)
  const s = score as 0 | 1 | 2 | 3 | 4
  return { score: s, label: ["Too weak", "Weak", "Fair", "Good", "Strong"][s]! }
}

const MESSAGES: Record<string, string> = {
  "auth.invalid_credentials": "Incorrect email or password.",
  "auth.locked_out": "Too many failed attempts. Try again in 10 minutes.",
  "profile.handle_taken": "That handle is already taken.",
  banned: "This account has been suspended.",
}

export function errorMessage(e: unknown): string {
  if (e instanceof Error && "code" in e) {
    const err = e as ApiError
    return MESSAGES[err.code] ?? err.detail ?? err.title
  }
  return e instanceof Error ? e.message : "Something went wrong."
}

/** Looks up a field error in a problem+json `errors` map, ignoring key case (ASP.NET may PascalCase them). */
export function fieldError(e: unknown, ...names: string[]): string | undefined {
  const errors = (e as ApiError | undefined)?.errors
  if (!errors) return undefined
  const wanted = names.map((n) => n.toLowerCase())
  for (const [k, v] of Object.entries(errors)) if (wanted.includes(k.toLowerCase()) && v[0]) return v[0]
  return undefined
}

/** True when a problem has field errors that were not mapped to any of the shown fields. */
export function hasUnmappedError(e: unknown, ...names: string[]): boolean {
  if (!e) return false
  const errors = (e as ApiError).errors
  if (!errors || Object.keys(errors).length === 0) return true
  return !names.some((n) => fieldError(e, n))
}

export const SCOPE_DESCRIPTIONS: Record<string, string> = {
  "forge:read": "Read your Forge library",
  "forge:write": "Create and edit items in your Forge library",
  "community:publish": "Publish content to the community on your behalf",
}

export const AVATAR_MAX_BYTES = 256 * 1024
export const AVATAR_MAX_DIM = 512
export const AVATAR_TYPES = ["image/webp", "image/png", "image/jpeg"]

export function checkAvatarFile(file: { type: string; size: number }): string | null {
  if (!AVATAR_TYPES.includes(file.type)) return "Use a WebP, PNG or JPEG image."
  if (file.size > AVATAR_MAX_BYTES) return "The image must be 256 KB or smaller."
  return null
}

export function checkAvatarSize(w: number, h: number): string | null {
  return w > AVATAR_MAX_DIM || h > AVATAR_MAX_DIM ? "The image must be at most 512 x 512 pixels." : null
}

export function handleError(e: unknown): string | undefined {
  return fieldError(e, "handle") ?? ((e as { code?: string } | null)?.code === "profile.handle_taken" ? MESSAGES["profile.handle_taken"] : undefined)
}
