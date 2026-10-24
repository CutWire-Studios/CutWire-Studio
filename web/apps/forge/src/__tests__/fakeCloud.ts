import { createHash } from "node:crypto"
import { vi } from "vitest"
import type { ForgeDoc } from "@cutwire/forge-core/doc/types"

interface Rec {
  id: string
  localId: string
  kind: string
  name: string
  revision: number
  updatedAt: string
  doc: ForgeDoc
}

interface Body {
  localId: string
  doc: ForgeDoc
  baseRevision: number
}

export interface Call {
  method: string
  path: string
  headers: Headers
  body?: Body
}

const json = (status: number, body: unknown) =>
  new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } })
const notFound = () => json(404, { code: "not_found", title: "nope" })

/** An in-memory implementation of the /api/v1/forge contract behind a stubbed global fetch. */
export function fakeCloud() {
  const docs = new Map<string, Rec>()
  const blobs = new Map<string, Uint8Array>()
  const calls: Call[] = []
  let n = 0
  let down = false

  const summary = (r: Rec) => ({ id: r.id, localId: r.localId, kind: r.kind, name: r.name, revision: r.revision, updatedAt: r.updatedAt, thumbUrl: null })
  const make = (localId: string, doc: ForgeDoc): Rec => {
    const r: Rec = { id: `c${++n}`, localId, kind: doc.kind, name: doc.meta.displayName, revision: 1, updatedAt: new Date().toISOString(), doc: structuredClone(doc) }
    docs.set(r.id, r)
    return r
  }

  const create = (body: Body) => {
    const dup = [...docs.values()].find((d) => d.localId === body.localId)
    if (dup) return json(409, { code: "document_exists", id: dup.id })
    if (body.doc.assets.some((a) => (a.data?.length ?? 0) > 1024)) return json(422, { code: "inline_asset_too_large", title: "inline" })
    return json(201, make(body.localId, body.doc))
  }

  const uploadAsset = async (init: RequestInit) => {
    const bytes = new Uint8Array(await new Response(init.body as BodyInit).arrayBuffer())
    const sha256 = createHash("sha256").update(bytes).digest("hex")
    blobs.set(sha256, bytes)
    return json(200, { sha256, url: `/files/${sha256}` })
  }

  const update = (r: Rec, body: Body) => {
    if (body.baseRevision !== r.revision) {
      return json(409, { code: "revision_conflict", title: "conflict", current: { revision: r.revision, updatedAt: r.updatedAt, doc: r.doc } })
    }
    r.doc = body.doc
    r.name = body.doc.meta.displayName
    r.revision++
    r.updatedAt = new Date().toISOString()
    return json(200, { revision: r.revision, updatedAt: r.updatedAt })
  }

  const one = (r: Rec | undefined, method: string, body: Body | undefined, thumb: boolean) => {
    if (!r) return notFound()
    if (thumb) return new Response(null, { status: 204 })
    if (method === "PUT") return update(r, body as Body)
    if (method === "DELETE") {
      docs.delete(r.id)
      return new Response(null, { status: 204 })
    }
    return json(200, r)
  }

  const handle = async (input: string | URL | Request, init: RequestInit = {}): Promise<Response> => {
    if (down) throw new TypeError("Failed to fetch")
    const path = new URL(String(input), "http://localhost").pathname
    const method = init.method ?? "GET"
    const body = typeof init.body === "string" ? (JSON.parse(init.body) as Body) : undefined
    calls.push({ method, path, headers: new Headers(init.headers), body })

    if (path === "/api/v1/forge/documents") return method === "POST" ? create(body as Body) : json(200, { documents: [...docs.values()].map(summary) })
    if (path === "/api/v1/forge/assets") return uploadAsset(init)
    if (path.startsWith("/files/")) {
      const b = blobs.get(path.slice(7))
      return b ? new Response(b as BodyInit, { status: 200 }) : notFound()
    }
    const m = /^\/api\/v1\/forge\/documents\/([^/]+)(\/thumb)?$/.exec(path)
    return m ? one(docs.get(m[1]), method, body, !!m[2]) : notFound()
  }

  const fetchMock = vi.fn(handle)
  vi.stubGlobal("fetch", fetchMock)

  return {
    docs,
    blobs,
    calls,
    fetchMock,
    setDown: (v: boolean) => (down = v),
    /** Another writer (MCP, AI, another tab) changes a document. */
    externalEdit(id: string, edit: (doc: ForgeDoc) => void) {
      const r = docs.get(id) as Rec
      edit(r.doc)
      r.name = r.doc.meta.displayName
      r.revision++
      r.updatedAt = new Date().toISOString()
      return r
    },
    add: make,
    callsTo: (method: string, pathRe: RegExp) => calls.filter((c) => c.method === method && pathRe.test(c.path)),
  }
}

type Listener = (e: { data: string }) => void

export class FakeEventSource {
  onopen: (() => void) | null = null
  closed = false
  private listeners = new Map<string, Listener[]>()
  constructor(readonly url: string) {}
  addEventListener(type: string, fn: Listener) {
    this.listeners.set(type, [...(this.listeners.get(type) ?? []), fn])
  }
  close() {
    this.closed = true
  }
  emit(type: string, data: unknown) {
    for (const fn of this.listeners.get(type) ?? []) fn({ data: JSON.stringify(data) })
  }
}
