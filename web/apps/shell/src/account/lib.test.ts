import { account } from "@cutwire/api-client"
import { afterEach, describe, expect, it, vi } from "vitest"
import { checkAvatarFile, checkAvatarSize, fieldError, fromBase64Url, passwordStrength, sanitizeReturn, signInOutcome, toBase64Url, toPublicKeyOptions, validateHandle } from "./lib"

const origin = "https://studio.test"

afterEach(() => vi.unstubAllGlobals())

describe("sanitizeReturn", () => {
  it("accepts same-origin paths with query and hash", () => {
    expect(sanitizeReturn("/connect/authorize?client_id=x&state=a%20b#h", "/", origin)).toBe("/connect/authorize?client_id=x&state=a%20b#h")
    expect(sanitizeReturn("/account", "/", origin)).toBe("/account")
  })
  it("normalises absolute same-origin URLs to a path", () => {
    expect(sanitizeReturn("https://studio.test/connect/authorize?a=1", "/", origin)).toBe("/connect/authorize?a=1")
  })
  it.each(["//evil.com", "/\\evil.com", "https://evil.com/x", "http://studio.test.evil.com/", "javascript:alert(1)", "evil.com", "/\t/evil.com", "///evil.com", "data:text/html,x"])(
    "rejects %s",
    (raw) => expect(sanitizeReturn(raw, "/", origin)).toBe("/"),
  )
  it("falls back for empty values", () => {
    expect(sanitizeReturn(null, "/home", origin)).toBe("/home")
    expect(sanitizeReturn("", "/home", origin)).toBe("/home")
  })
})

describe("base64url and WebAuthn options", () => {
  it("round-trips bytes without padding", () => {
    const bytes = new Uint8Array([251, 255, 190, 0, 1, 2, 3])
    const s = toBase64Url(bytes)
    expect(s).not.toMatch(/[+/=]/)
    expect([...fromBase64Url(s)]).toEqual([...bytes])
  })
  it("converts challenge, user id and credential ids", () => {
    const id = toBase64Url(new Uint8Array([1, 2, 3]))
    const opts = toPublicKeyOptions({
      challenge: id,
      user: { id, name: "a", displayName: "A" },
      allowCredentials: [{ type: "public-key", id }],
      excludeCredentials: [{ type: "public-key", id }],
      rpId: "studio.test",
    })
    expect([...(opts.challenge as Uint8Array)]).toEqual([1, 2, 3])
    expect([...(opts.user.id as Uint8Array)]).toEqual([1, 2, 3])
    expect([...(opts.allowCredentials[0].id as Uint8Array)]).toEqual([1, 2, 3])
    expect([...(opts.excludeCredentials[0].id as Uint8Array)]).toEqual([1, 2, 3])
    expect(opts.rpId).toBe("studio.test")
  })
  it("unwraps a publicKey envelope", () => {
    const opts = toPublicKeyOptions({ publicKey: { challenge: toBase64Url(new Uint8Array([9])) } })
    expect([...(opts.challenge as Uint8Array)]).toEqual([9])
  })
})

describe("validators", () => {
  it("validates handles", () => {
    expect(validateHandle("ab")).toMatch(/at least 3/)
    expect(validateHandle("a".repeat(25))).toMatch(/at most 24/)
    expect(validateHandle("bad handle")).toMatch(/Only/)
    expect(validateHandle("Good_Name-1")).toBeNull()
    expect(validateHandle("abc")).toBeNull()
  })
  it("scores passwords", () => {
    expect(passwordStrength("short").score).toBeLessThanOrEqual(1)
    expect(passwordStrength("Correct-Horse-Battery-9").score).toBe(4)
  })
  it("checks avatars", () => {
    expect(checkAvatarFile({ type: "image/gif", size: 10 })).not.toBeNull()
    expect(checkAvatarFile({ type: "image/png", size: 256 * 1024 + 1 })).not.toBeNull()
    expect(checkAvatarFile({ type: "image/png", size: 1000 })).toBeNull()
    expect(checkAvatarSize(513, 100)).not.toBeNull()
    expect(checkAvatarSize(512, 512)).toBeNull()
  })
  it("maps field errors case-insensitively", () => {
    expect(fieldError({ errors: { Handle: ["taken"] } }, "handle")).toBe("taken")
    expect(fieldError({ errors: {} }, "handle")).toBeUndefined()
  })
})

describe("sign-in status routing", () => {
  const mockStatus = (status: string) =>
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(JSON.stringify({ status }), { status: 200, headers: { "Content-Type": "application/json" } })))

  it.each([
    ["ok", { kind: "done", to: "/next" }],
    ["requires_2fa", { kind: "two-factor" }],
    ["requires_verification", { kind: "verify" }],
    ["pending_profile", { kind: "pending-profile", to: "/account/pending-profile?return=%2Fnext" }],
    ["weird", { kind: "unknown", status: "weird" }],
  ])("routes %s", async (status, expected) => {
    mockStatus(status)
    const res = await account.signIn({ email: "a@b.c", password: "x" })
    expect(signInOutcome(res.status, "/next")).toEqual(expected)
    const [url, init] = (fetch as unknown as ReturnType<typeof vi.fn>).mock.calls[0]!
    expect(url).toBe("/api/v1/account/sign-in")
    expect(init.method).toBe("POST")
    expect((init.headers as Headers).get("X-Requested-With")).toBeTruthy()
    expect(init.credentials).toBe("include")
  })

  it("surfaces invalid credentials as an ApiError", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(JSON.stringify({ title: "Unauthorized", code: "auth.invalid_credentials" }), { status: 401 })))
    await expect(account.signIn({ email: "a@b.c", password: "x" })).rejects.toMatchObject({ status: 401, code: "auth.invalid_credentials" })
  })
})
