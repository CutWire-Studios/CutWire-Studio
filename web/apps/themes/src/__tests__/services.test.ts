import "fake-indexeddb/auto"
import { unzipSync } from "fflate"
import { createStore, get } from "idb-keyval"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"
import { deleteDraft, listDrafts, loadDraftsRemote, resetRemoteState, saveDraft, saveDraftRemote } from "../services/drafts"
import { buildHandoff, buildPackage, HANDOFF_DB, HANDOFF_KEY, HANDOFF_STORE, writeHandoff } from "../services/publish"

function respond(routes: Record<string, { status: number; body?: unknown }>) {
  const calls: string[] = []
  vi.stubGlobal(
    "fetch",
    vi.fn(async (url: string, init?: RequestInit) => {
      calls.push(`${init?.method ?? "GET"} ${url}`)
      const r = routes[url] ?? { status: 404 }
      return new Response(r.body === undefined ? null : JSON.stringify(r.body), { status: r.status, headers: { "Content-Type": "application/json" } })
    }),
  )
  return calls
}

const draft = { id: "d1", name: "mine", doc: { name: "mine" }, updatedAt: 5 }

describe("drafts", () => {
  beforeEach(() => resetRemoteState())
  afterEach(() => vi.unstubAllGlobals())

  it("does nothing remotely while signed out", async () => {
    const calls = respond({ "/api/v1/me": { status: 401 } })
    await saveDraftRemote(draft)
    expect(await loadDraftsRemote()).toEqual([])
    expect(calls.every((c) => c === "GET /api/v1/me")).toBe(true)
  })

  it("calls the drafts endpoint when signed in and tolerates its absence", async () => {
    const calls = respond({ "/api/v1/me": { status: 200, body: { id: "u", userName: "a" } } })
    await saveDraftRemote(draft)
    await saveDraftRemote(draft)
    expect(await loadDraftsRemote()).toEqual([])
    expect(calls.filter((c) => c.startsWith("PUT")).length).toBe(1)
  })

  it("reads remote drafts when the endpoint exists and merges newest-wins with local ones", async () => {
    respond({
      "/api/v1/me": { status: 200, body: { id: "u", userName: "a" } },
      "/api/v1/themes/drafts": { status: 200, body: { items: [{ ...draft, name: "remote", updatedAt: 99 }, { ...draft, id: "d2", updatedAt: 1 }] } },
      "/api/v1/themes/drafts/d1": { status: 204 },
    })
    await saveDraft(draft)
    const all = await listDrafts()
    expect(all.map((d) => [d.id, d.name])).toEqual([["d1", "remote"], ["d2", "mine"]])
    await deleteDraft("d1")
  })

  it("keeps drafts locally without any network", async () => {
    respond({ "/api/v1/me": { status: 401 } })
    await saveDraft({ ...draft, id: "local-only", updatedAt: 7 })
    expect((await listDrafts()).some((d) => d.id === "local-only")).toBe(true)
    await deleteDraft("local-only")
    expect((await listDrafts()).some((d) => d.id === "local-only")).toBe(false)
  })
})

describe("publish package and handoff", () => {
  it("packages overrides only, as json", async () => {
    const pkg = buildPackage({ name: "p", extends: "dark", variant: "dark", palette: { primary: "#ff0000", accent: "#242424" } })
    expect(pkg.fileName).toBe("p.json")
    const doc = JSON.parse(await pkg.blob.text())
    expect(doc.palette).toEqual({ primary: "#ff0000" })
    expect(doc.schemaVersion).toBe(1)
  })

  it("packages dual-variant themes as a zip without qml", async () => {
    const pkg = buildPackage({ name: "Two Tone", extends: "dark", variants: { dark: { palette: { primary: "#111111" } }, light: { palette: { primary: "#eeeeee" } } } })
    expect(pkg.fileName).toBe("two-tone.zip")
    const files = unzipSync(new Uint8Array(await pkg.blob.arrayBuffer()))
    expect(Object.keys(files).sort()).toEqual(["theme.dark.json", "theme.json", "theme.light.json"])
  })

  it("writes the handoff where the upload page will look for it", async () => {
    const shot = new Blob(["x"], { type: "image/webp" })
    const h = buildHandoff({ name: "p", extends: "light", variant: "light" }, shot)
    await writeHandoff(h)
    const back = await get(HANDOFF_KEY, createStore(HANDOFF_DB, HANDOFF_STORE))
    expect(back).toMatchObject({ version: 1, kind: "theme", meta: { name: "p", variant: "light", extends: "light", dual: false }, screenshot: { width: 1280, height: 720, mime: "image/webp" }, package: { fileName: "p.json" } })
  })
})
