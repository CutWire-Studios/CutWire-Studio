import { api, type Schemas } from "./client"

type Body = Record<string, unknown> | undefined

const send = <T = void>(method: string, path: string, body?: Body) =>
  api<T>(path, { method, body: body === undefined ? undefined : JSON.stringify(body) })

export type SignInStatus = "ok" | "requires_verification" | "requires_2fa" | "pending_profile"
export type StatusResponse<S extends string = string> = { status: S }

export type Profile = Schemas["ProfileDto"]
export type TwoFactorStatus = Schemas["TwoFactorStatus"]
export type TwoFactorSetup = Schemas["TwoFactorSetup"]
export type Passkey = Schemas["PasskeyDto"]
export type Pat = Schemas["PatDto"]
export type CreatedPat = Schemas["CreatedPatDto"]
export type ConnectedApp = Schemas["ConnectedAppDto"]
export type ConsentInfo = Schemas["ConsentInfo"]
export type ScopeInfo = Schemas["ScopeInfo"]
export type ExternalProvider = Schemas["ExternalProviderDto"]
export type PendingProfile = Schemas["PendingProfileDto"]

export const PAT_SCOPES = ["forge:read", "forge:write", "community:publish"] as const

// The WebAuthn option endpoints are typed as `string` in openapi.json but return the options object itself.
const asObject = <T>(v: unknown) => (typeof v === "string" ? (JSON.parse(v) as T) : (v as T))

export const account = {
  signUp: (b: { email: string; password: string; handle: string; displayName?: string }) =>
    send<StatusResponse>("POST", "/account/sign-up", b),
  signIn: (b: { email: string; password: string; rememberMe?: boolean }) =>
    send<StatusResponse<SignInStatus>>("POST", "/account/sign-in", b),
  signIn2fa: (b: { code?: string; recoveryCode?: string; rememberMe?: boolean; rememberMachine?: boolean }) =>
    send<StatusResponse<SignInStatus>>("POST", "/account/sign-in-2fa", b),
  signOut: () => send("POST", "/account/sign-out"),
  verifyEmail: (b: { userId: string; token: string }) => send("POST", "/account/verify-email", b),
  resendVerification: (email: string) => send("POST", "/account/resend-verification", { email }),
  forgotPassword: (email: string) => send("POST", "/account/forgot-password", { email }),
  resetPassword: (b: { email: string; token: string; newPassword: string }) => send("POST", "/account/reset-password", b),
  confirmEmailChange: (b: { userId: string; email: string; token: string }) => send("POST", "/account/confirm-email-change", b),
  passkeyRequestOptions: async () => asObject<Record<string, unknown>>(await send<unknown>("POST", "/account/passkeys/request-options")),
  passkeySignIn: (credential: unknown) => send<StatusResponse<SignInStatus>>("POST", "/account/passkeys/sign-in", { credential }),

  externalProviders: () => api<ExternalProvider[]>("/account/external"),
  pendingProfile: () => api<PendingProfile>("/account/external/pending"),
  completeProfile: (b: { handle: string; displayName?: string }) => send<StatusResponse>("POST", "/account/external/complete", b),

  changePassword: (b: { currentPassword?: string; newPassword: string }) => send("POST", "/account/change-password", b),
  changeEmail: (b: { newEmail: string; password: string }) => send("POST", "/account/change-email", b),
  signOutEverywhere: () => send("POST", "/account/sessions/sign-out-everywhere"),
  deleteAccount: (password: string) => send("POST", "/account/delete", { password }),

  profile: () => api<Profile>("/account/profile"),
  updateProfile: (b: { handle: string; displayName: string; bio: string }) => send<Profile>("PUT", "/account/profile", b),
  uploadAvatar: (file: Blob) =>
    api<Profile>("/account/profile/avatar", { method: "POST", body: file, headers: { "Content-Type": file.type } }),
  deleteAvatar: () => send<Profile>("DELETE", "/account/profile/avatar"),

  twoFactor: () => api<TwoFactorStatus>("/account/2fa"),
  twoFactorSetup: () => send<TwoFactorSetup>("POST", "/account/2fa/setup"),
  twoFactorEnable: (code: string) => send<{ recoveryCodes: string[] }>("POST", "/account/2fa/enable", { code }),
  twoFactorDisable: (password: string) => send("POST", "/account/2fa/disable", { password }),
  recoveryCodes: (password: string) => send<{ recoveryCodes: string[] }>("POST", "/account/2fa/recovery-codes", { password }),

  passkeyCreationOptions: async () => asObject<Record<string, unknown>>(await send<unknown>("POST", "/account/passkeys/creation-options")),
  passkeys: () => api<Passkey[]>("/account/passkeys"),
  addPasskey: (credential: unknown, name?: string) => send<Passkey>("POST", "/account/passkeys", { credential, name }),
  renamePasskey: (id: string, name: string) => send<Passkey>("PATCH", `/account/passkeys/${encodeURIComponent(id)}`, { name }),
  deletePasskey: (id: string) => send("DELETE", `/account/passkeys/${encodeURIComponent(id)}`),

  tokens: () => api<Pat[]>("/account/tokens"),
  createToken: (b: { name: string; scopes: string[]; expiresAt?: string }) => send<CreatedPat>("POST", "/account/tokens", b),
  revokeToken: (id: string) => send("DELETE", `/account/tokens/${encodeURIComponent(id)}`),

  connectedApps: () => api<ConnectedApp[]>("/account/connected-apps"),
  revokeConnectedApp: (id: string) => send("DELETE", `/account/connected-apps/${encodeURIComponent(id)}`),

  consentInfo: (returnUrl: string) => api<ConsentInfo>(`/account/consent-info?return=${encodeURIComponent(returnUrl)}`),
  consent: (returnUrl: string, approve: boolean) => send<{ redirect: string }>("POST", "/account/consent", { return: returnUrl, approve }),
}

export async function deviceDecision(userCode: string, accept: boolean): Promise<"approved" | "denied"> {
  const res = await fetch("/connect/verify", {
    method: "POST",
    credentials: "same-origin",
    redirect: "manual",
    headers: { "X-Requested-With": "cutwire", "Content-Type": "application/x-www-form-urlencoded" },
    body: new URLSearchParams({ user_code: userCode, submit: accept ? "accept" : "deny" }),
  })
  // A manual redirect surfaces as an opaque response; the outcome is implied by the decision.
  if (res.type === "opaqueredirect" || res.ok || (res.status >= 300 && res.status < 400)) return accept ? "approved" : "denied"
  throw new Error("Could not complete the request. The code may have expired.")
}
