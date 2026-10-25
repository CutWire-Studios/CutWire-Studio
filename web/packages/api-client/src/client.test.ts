import { afterEach, describe, expect, it, vi } from "vitest"
import { ApiError, api, installId, me, parseProblem } from "./client"

const problem = (status: number, body: unknown) =>
  new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/problem+json" } })

afterEach(() => vi.unstubAllGlobals())

describe("api client", () => {
  it("parses problem+json into ApiError", async () => {
    const err = await parseProblem(
      problem(422, { title: "Invalid", code: "quality.failed", detail: "Fix it", errors: { name: ["required"] } }),
    )
    expect(err).toBeInstanceOf(ApiError)
    expect(err).toMatchObject({ status: 422, code: "quality.failed", title: "Invalid", detail: "Fix it", errors: { name: ["required"] } })
  })

  it("falls back for non-JSON error bodies", async () => {
    const err = await parseProblem(new Response("bad gateway", { status: 502, statusText: "Bad Gateway" }))
    expect(err).toMatchObject({ status: 502, code: "unknown", title: "Bad Gateway" })
  })

  it("sends credentials and the CSRF header", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response("{}", { status: 200 }))
    vi.stubGlobal("fetch", fetchMock)
    await api("/ping")
    const [url, init] = fetchMock.mock.calls[0]!
    expect(url).toBe("/api/v1/ping")
    expect(init.credentials).toBe("include")
    expect((init.headers as Headers).get("X-Requested-With")).toBe("cutwire")
  })

  it("me() returns null on 401 and throws on other errors", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(problem(401, { title: "Unauthorized", code: "auth.required" })))
    expect(await me()).toBeNull()
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(problem(500, { title: "Boom", code: "server" })))
    await expect(me()).rejects.toMatchObject({ status: 500 })
  })

  it("generates the install id once, persists it and sends it on every request", async () => {
    const store = new Map<string, string>()
    vi.stubGlobal("localStorage", {
      getItem: (k: string) => store.get(k) ?? null,
      setItem: (k: string, v: string) => void store.set(k, v),
    })
    const first = installId()
    expect(first).toMatch(/^[0-9a-f-]{36}$/)
    expect(store.get("studio-install-id")).toBe(first)
    expect(installId()).toBe(first)

    const fetchMock = vi.fn().mockResolvedValue(new Response("{}", { status: 200 }))
    vi.stubGlobal("fetch", fetchMock)
    await api("/stock/catalog")
    expect((fetchMock.mock.calls[0]![1].headers as Headers).get("X-Studio-Install")).toBe(first)
  })

  it("reuses an install id already in storage", () => {
    vi.stubGlobal("localStorage", { getItem: () => "existing-id", setItem: vi.fn() })
    expect(installId()).toBe("existing-id")
  })
})
