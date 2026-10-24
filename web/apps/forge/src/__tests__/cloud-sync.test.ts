import "fake-indexeddb/auto"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"
import type { ForgeDoc } from "@cutwire/forge-core/doc/types"
import { emptyDoc, bytesToBase64 } from "@cutwire/forge-core/doc/util"
import type { SyncAsset } from "@/services/cloud/assets"
import { SyncEngine, type EditorBridge } from "@/services/cloud/engine"
import { docHash } from "@/services/cloud/hash"
import { resetSyncStore } from "@/services/cloud/engine"
import { mergeLibrary, syncStateOf, useSync } from "@/services/cloud/store"
import { deleteEntry, listLibrary, loadEntry, saveDoc, saveEntry, type LibraryEntry } from "@/services/storage/library"
import { useEditor } from "@/state/editor"
import { fakeCloud, FakeEventSource } from "./fakeCloud"

const until = async (cond: () => boolean | Promise<boolean>) => {
  for (let i = 0; i < 400; i++) {
    if (await cond()) return
    await new Promise((r) => setImmediate(r))
  }
  throw new Error("timed out waiting for condition")
}

const rename = (doc: ForgeDoc, name: string): ForgeDoc => ({ ...structuredClone(doc), meta: { ...doc.meta, displayName: name } })

function setup(opts: { bridge?: EditorBridge } = {}) {
  const cloud = fakeCloud()
  const notes: string[] = []
  const sources: FakeEventSource[] = []
  const engine = new SyncEngine({
    clientId: "tab-1",
    notify: (t) => void notes.push(t),
    bridge: opts.bridge,
    openEvents: (url) => {
      const es = new FakeEventSource(url)
      sources.push(es)
      return es
    },
  })
  return { cloud, engine, notes, sources }
}

/** A local entry already linked to a cloud document at revision 1. */
async function linked(cloud: ReturnType<typeof fakeCloud>, localId = "d1", doc = emptyDoc("effect", "Glow")) {
  const rec = cloud.add(localId, doc)
  const entry: LibraryEntry = {
    localId,
    doc,
    updatedAt: 1,
    cloudId: rec.id,
    revision: rec.revision,
    syncedAt: 1,
    syncedHash: docHash(doc),
    dirty: false,
  }
  await saveEntry(entry)
  return { rec, entry }
}

beforeEach(async () => {
  vi.useFakeTimers({ toFake: ["setTimeout", "clearTimeout"] })
  resetSyncStore()
  useSync.setState({ signedIn: true })
  for (const e of await listLibrary()) await deleteEntry(e.localId)
})

afterEach(() => {
  vi.useRealTimers()
  vi.unstubAllGlobals()
})

describe("library merge", () => {
  it("links local and cloud documents by localId or cloud id and appends cloud-only ones", () => {
    const a = emptyDoc("effect", "A")
    const b = emptyDoc("effect", "B")
    const local: LibraryEntry[] = [
      { localId: "l1", doc: a, updatedAt: 30 },
      { localId: "l2", doc: b, updatedAt: 20, cloudId: "c2" },
    ]
    const cloud = [
      { id: "c1", localId: "l1", kind: "effect", name: "A", revision: 1, updatedAt: new Date(10).toISOString(), thumbUrl: null },
      { id: "c2", localId: "other", kind: "effect", name: "B", revision: 3, updatedAt: new Date(20).toISOString(), thumbUrl: null },
      { id: "c3", localId: "l3", kind: "transition", name: "C", revision: 1, updatedAt: new Date(25).toISOString(), thumbUrl: null },
    ]
    const items = mergeLibrary(local, cloud)
    expect(items.map((i) => [i.kind, i.localId])).toEqual([
      ["local", "l1"],
      ["cloud", "l3"],
      ["local", "l2"],
    ])
    expect(items[0].kind === "local" && items[0].cloud?.id).toBe("c1")
    expect(items[2].kind === "local" && items[2].cloud?.id).toBe("c2")
  })

  it("shows local entries alone when there is no cloud list", () => {
    expect(mergeLibrary([{ localId: "l1", doc: emptyDoc("effect"), updatedAt: 1 }], null)).toHaveLength(1)
  })

  it("derives the sync state", () => {
    const meta = { cloudId: "c", dirty: false }
    expect(syncStateOf(undefined, undefined, true)).toBe("local")
    expect(syncStateOf(meta, undefined, false)).toBe("local")
    expect(syncStateOf(meta, undefined, true)).toBe("synced")
    expect(syncStateOf({ ...meta, dirty: true }, undefined, true)).toBe("pending")
    expect(syncStateOf(meta, { phase: "syncing" }, true)).toBe("syncing")
    expect(syncStateOf(meta, { phase: "offline" }, true)).toBe("offline")
    expect(syncStateOf(meta, { phase: "conflict" }, true)).toBe("conflict")
  })

  it("fingerprints documents regardless of key order", () => {
    const d = emptyDoc("effect", "X")
    const reordered = Object.fromEntries(Object.entries(d).reverse()) as unknown as ForgeDoc
    expect(docHash(reordered)).toBe(docHash(d))
    expect(docHash(rename(d, "Y"))).not.toBe(docHash(d))
  })
})

describe("pushing", () => {
  it("debounces edits into one PUT with the base revision and the tab's client id", async () => {
    const { cloud, engine } = setup()
    const { rec, entry } = await linked(cloud)
    const first = await saveDoc("d1", rename(entry.doc, "Glow 2"))
    engine.noteSaved(first)
    await vi.advanceTimersByTimeAsync(1000)
    const second = await saveDoc("d1", rename(entry.doc, "Glow 3"))
    engine.noteSaved(second)
    await vi.advanceTimersByTimeAsync(1499)
    expect(cloud.callsTo("PUT", /documents\/[^/]+$/)).toHaveLength(0)
    await vi.advanceTimersByTimeAsync(1)
    await until(async () => (await loadEntry("d1"))?.revision === 2)

    const puts = cloud.callsTo("PUT", /documents\/[^/]+$/)
    expect(puts).toHaveLength(1)
    expect(puts[0].body!.baseRevision).toBe(1)
    expect(puts[0].body!.doc.meta.displayName).toBe("Glow 3")
    expect(puts[0].headers.get("X-Forge-Client")).toBe("tab-1")
    expect(cloud.docs.get(rec.id)!.revision).toBe(2)
    const saved = (await loadEntry("d1"))!
    expect(saved.dirty).toBe(false)
    expect(saved.syncedHash).toBe(docHash(second.doc))
  })

  it("does nothing when signed out", async () => {
    const { cloud, engine } = setup()
    await linked(cloud)
    useSync.setState({ signedIn: false })
    engine.schedulePush("d1", 0)
    await vi.advanceTimersByTimeAsync(5000)
    expect(cloud.callsTo("PUT", /documents/)).toHaveLength(0)
  })

  it("uploads assets before the push and strips their data from the payload only", async () => {
    const { cloud, engine } = setup()
    const doc = emptyDoc("effect", "Pics")
    const bytes = new Uint8Array(3000).map((_, i) => i % 251)
    doc.assets.push({ id: "a1", name: "pic.png", mime: "image/png", data: bytesToBase64(bytes), width: 4, height: 4 })
    await linked(cloud, "d1", doc)
    const edited = await saveDoc("d1", rename(doc, "Pics 2"))
    engine.noteSaved(edited)
    await vi.advanceTimersByTimeAsync(1500)
    await until(async () => (await loadEntry("d1"))?.revision === 2)

    const order = cloud.calls.filter((c) => c.method !== "GET").map((c) => `${c.method} ${c.path}`)
    expect(order.indexOf("POST /api/v1/forge/assets")).toBeGreaterThanOrEqual(0)
    expect(order.indexOf("POST /api/v1/forge/assets")).toBeLessThan(order.findIndex((o) => o.startsWith("PUT /api/v1/forge/documents/")))
    const put = cloud.callsTo("PUT", /documents\/[^/]+$/)[0]
    const sent = put.body!.doc.assets[0] as unknown as SyncAsset
    expect(sent.data).toBeUndefined()
    expect(sent.blob).toMatch(/^[0-9a-f]{64}$/)
    expect(cloud.blobs.has(sent.blob as string)).toBe(true)

    const local = (await loadEntry("d1"))!.doc.assets[0] as unknown as SyncAsset
    expect(local.data).toBe(bytesToBase64(bytes))
    expect(local.blob).toBe(sent.blob)
  })

  it("retries with backoff while offline and recovers", async () => {
    const { cloud, engine } = setup()
    await linked(cloud)
    cloud.setDown(true)
    engine.noteSaved(await saveDoc("d1", rename(emptyDoc("effect", "Glow"), "Offline edit")))
    await vi.advanceTimersByTimeAsync(1500)
    await until(() => useSync.getState().runtime.d1?.phase === "offline")
    cloud.setDown(false)
    await vi.advanceTimersByTimeAsync(2000)
    await until(async () => (await loadEntry("d1"))?.revision === 2)
    expect(useSync.getState().runtime.d1?.phase).toBe("idle")
  })
})

describe("conflicts", () => {
  async function conflicted() {
    const bridge: EditorBridge = { isOpen: vi.fn(() => true), currentDoc: () => null, apply: vi.fn() }
    const ctx = setup({ bridge })
    const { rec, entry } = await linked(ctx.cloud)
    ctx.cloud.externalEdit(rec.id, (d) => void (d.meta.displayName = "Server name"))
    ctx.engine.noteSaved(await saveDoc("d1", rename(entry.doc, "My name")))
    await vi.advanceTimersByTimeAsync(1500)
    await until(() => !!useSync.getState().conflicts.d1)
    return { ...ctx, rec, bridge }
  }

  it("raises a conflict on 409 and stops pushing", async () => {
    const { cloud } = await conflicted()
    const c = useSync.getState().conflicts.d1
    expect(c.current.revision).toBe(2)
    expect(useSync.getState().activeConflict).toBe("d1")
    expect(useSync.getState().runtime.d1.phase).toBe("conflict")
    expect(cloud.callsTo("PUT", /documents\/[^/]+$/)).toHaveLength(1)
  })

  it("Keep mine overwrites using the server's revision as the base", async () => {
    const { cloud, engine, rec } = await conflicted()
    await engine.resolve("d1", "mine")
    const puts = cloud.callsTo("PUT", /documents\/[^/]+$/)
    expect(puts).toHaveLength(2)
    expect(puts[1].body!.baseRevision).toBe(2)
    expect(cloud.docs.get(rec.id)!.doc.meta.displayName).toBe("My name")
    expect(useSync.getState().conflicts.d1).toBeUndefined()
  })

  it("Take server version replaces the local copy and applies it to the editor", async () => {
    const { engine, bridge } = await conflicted()
    await engine.resolve("d1", "server")
    const e = (await loadEntry("d1"))!
    expect(e.doc.meta.displayName).toBe("Server name")
    expect(e.revision).toBe(2)
    expect(e.dirty).toBe(false)
    expect(bridge.apply).toHaveBeenCalledTimes(1)
    expect((bridge.apply as ReturnType<typeof vi.fn>).mock.calls[0][1].meta.displayName).toBe("Server name")
    expect(useSync.getState().conflicts.d1).toBeUndefined()
  })

  it("Keep both saves mine as a copy (and uploads it) then takes the server version", async () => {
    const { engine, cloud } = await conflicted()
    await engine.resolve("d1", "both")
    expect((await loadEntry("d1"))!.doc.meta.displayName).toBe("Server name")
    const all = await listLibrary()
    const copy = all.find((e) => e.localId !== "d1")!
    expect(copy.doc.meta.displayName).toBe("My name (copy)")
    await until(async () => !!(await loadEntry(copy.localId))?.cloudId)
    expect(cloud.callsTo("POST", /documents$/)).toHaveLength(1)
  })
})

describe("live updates", () => {
  const openBridge = (current: () => ForgeDoc | null) => {
    const apply = vi.fn()
    const bridge: EditorBridge = { isOpen: () => true, currentDoc: current, apply }
    return { bridge, apply }
  }

  it("ignores the echo of this tab's own writes", async () => {
    const { apply, bridge } = openBridge(() => null)
    const { cloud, engine, sources, notes } = setup({ bridge })
    const { rec } = await linked(cloud)
    engine.subscribe("d1", rec.id)
    cloud.fetchMock.mockClear()
    sources[0].emit("updated", { revision: 2, updatedAt: "x", source: "web", clientId: "tab-1" })
    await vi.advanceTimersByTimeAsync(10)
    expect(cloud.fetchMock).not.toHaveBeenCalled()
    expect(apply).not.toHaveBeenCalled()
    expect(notes).toEqual([])
  })

  it("applies an external update when there are no local edits, and says who made it", async () => {
    const { apply, bridge } = openBridge(() => (useEditor.getState().doc))
    const { cloud, engine, sources, notes } = setup({ bridge })
    const { rec, entry } = await linked(cloud)
    useEditor.getState().load("d1", entry.doc)
    await engine.loadMeta("d1")
    engine.subscribe("d1", rec.id)
    const r = cloud.externalEdit(rec.id, (d) => void (d.meta.displayName = "From MCP"))
    sources[0].emit("updated", { revision: r.revision, updatedAt: r.updatedAt, source: "mcp", clientId: null })
    await until(() => apply.mock.calls.length > 0)
    expect(apply.mock.calls[0][1].meta.displayName).toBe("From MCP")
    expect(notes).toEqual(["Updated by MCP"])
    const e = (await loadEntry("d1"))!
    expect(e.revision).toBe(2)
    expect(e.dirty).toBe(false)
  })

  it("raises a conflict instead of applying when there are unsynced edits", async () => {
    const edited = emptyDoc("effect", "Glow")
    edited.meta.displayName = "Typing…"
    const { apply, bridge } = openBridge(() => edited)
    const { cloud, engine, sources } = setup({ bridge })
    const { rec } = await linked(cloud)
    await engine.loadMeta("d1")
    engine.subscribe("d1", rec.id)
    const r = cloud.externalEdit(rec.id, (d) => void (d.meta.displayName = "From AI"))
    sources[0].emit("updated", { revision: r.revision, updatedAt: r.updatedAt, source: "ai", clientId: null })
    await until(() => !!useSync.getState().conflicts.d1)
    expect(apply).not.toHaveBeenCalled()
    expect(useSync.getState().conflicts.d1.current.revision).toBe(2)
  })

  it("offers to keep a local copy when the document is deleted", async () => {
    const { bridge } = openBridge(() => null)
    const { cloud, engine, sources } = setup({ bridge })
    const { rec } = await linked(cloud)
    engine.subscribe("d1", rec.id)
    sources[0].emit("deleted", {})
    expect(useSync.getState().deleted?.localId).toBe("d1")
    await engine.keepAsLocalCopy("d1")
    const e = (await loadEntry("d1"))!
    expect(e.cloudId).toBeUndefined()
    expect(useSync.getState().deleted).toBeNull()
  })

  it("applies a remote document as ONE undo step", () => {
    const before = emptyDoc("effect", "Before")
    const after = emptyDoc("effect", "After")
    useEditor.getState().load("d1", before)
    useEditor.getState().applyRemote(after)
    const t = useEditor.temporal.getState()
    expect(useEditor.getState().doc).toBe(after)
    expect(t.pastStates).toHaveLength(1)
    t.undo()
    expect(useEditor.getState().doc).toBe(before)
    expect(useEditor.temporal.getState().futureStates).toHaveLength(1)
  })
})

describe("linking and hydrating", () => {
  it("uploads the local library, adopting documents that already exist", async () => {
    const { cloud, engine } = setup()
    const fresh = emptyDoc("effect", "Fresh")
    const dup = emptyDoc("effect", "Dup")
    await saveEntry({ localId: "n1", doc: fresh, updatedAt: 2 })
    await saveEntry({ localId: "n2", doc: dup, updatedAt: 1 })
    cloud.add("n2", dup)
    const result = await engine.uploadLocalLibrary()
    expect(result).toEqual({ created: 1, adopted: 1, conflict: 0, failed: 0 })
    expect((await loadEntry("n1"))?.cloudId).toBeTruthy()
    expect((await loadEntry("n2"))?.cloudId).toBeTruthy()
  })

  it("flags a conflict when the existing cloud document differs", async () => {
    const { cloud, engine } = setup()
    await saveEntry({ localId: "n1", doc: emptyDoc("effect", "Mine"), updatedAt: 2 })
    cloud.add("n1", emptyDoc("effect", "Theirs"))
    expect(await engine.link("n1")).toBe("conflict")
    expect(useSync.getState().conflicts.n1.name).toBe("Mine")
  })

  it("hydrates cloud-only documents, fetching their blobs into local data", async () => {
    const { cloud, engine } = setup()
    const bytes = new Uint8Array(2000).map((_, i) => (i * 7) % 255)
    const up = await fetch("/api/v1/forge/assets", { method: "POST", body: bytes, headers: { "Content-Type": "image/png" } })
    const { sha256 } = (await up.json()) as { sha256: string }
    const doc = emptyDoc("effect", "Remote")
    doc.assets.push({ id: "a1", name: "p.png", mime: "image/png", blob: sha256, width: 2, height: 2 } as unknown as ForgeDoc["assets"][number])
    cloud.add("r1", doc)
    await engine.refresh()
    const e = (await loadEntry("r1"))!
    expect(e.cloudId).toBeTruthy()
    expect(e.doc.assets[0].data).toBe(bytesToBase64(bytes))
    expect(e.dirty).toBe(false)
    expect(e.syncedHash).toBe(docHash(e.doc))
  })

  it("pulls a newer cloud revision when nothing is edited locally", async () => {
    const { cloud, engine } = setup()
    const { rec } = await linked(cloud)
    cloud.externalEdit(rec.id, (d) => void (d.meta.displayName = "Newer"))
    await engine.refresh()
    expect((await loadEntry("d1"))!.doc.meta.displayName).toBe("Newer")
  })
})
