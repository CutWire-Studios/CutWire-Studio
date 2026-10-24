import { api, parseProblem } from "./client"

export interface ForgeDocSummary {
  id: string
  localId: string
  kind: string
  name: string
  revision: number
  updatedAt: string
  thumbUrl: string | null
}

export interface ForgeDocRecord<D = unknown> extends Omit<ForgeDocSummary, "thumbUrl"> {
  doc: D
}

export interface ForgeUpdated {
  revision: number
  updatedAt: string
}

export interface ForgeConflict<D = unknown> {
  revision: number
  updatedAt: string
  doc: D
}

export type ForgeEvent =
  | { type: "updated"; revision: number; updatedAt: string; source: "web" | "mcp" | "ai"; clientId: string | null }
  | { type: "deleted" }

/** 409 `revision_conflict`: the server copy moved on; `current` is what it holds now. */
export class RevisionConflictError<D = unknown> extends Error {
  constructor(readonly current: ForgeConflict<D>) {
    super("The cloud copy changed since you last synced.")
    this.name = "RevisionConflictError"
  }
}

/** 409 `document_exists`: a document with this localId is already in the cloud. */
export class DocumentExistsError extends Error {
  constructor(readonly id: string) {
    super("This document is already in the cloud.")
    this.name = "DocumentExistsError"
  }
}

const enc = encodeURIComponent
const json = (body: unknown) => JSON.stringify(body)

async function raw(path: string, init: RequestInit): Promise<Response> {
  const headers = new Headers(init.headers)
  headers.set("X-Requested-With", "cutwire")
  return fetch(`/api/v1${path}`, { ...init, headers, credentials: "include" })
}

async function conflictBody(res: Response): Promise<Record<string, unknown>> {
  try {
    return (await res.clone().json()) as Record<string, unknown>
  } catch {
    return {}
  }
}

export const forge = {
  list: () => api<{ documents: ForgeDocSummary[] }>("/forge/documents").then((r) => r.documents),
  get: <D = unknown>(id: string) => api<ForgeDocRecord<D>>(`/forge/documents/${enc(id)}`),

  async create<D = unknown>(localId: string, doc: D): Promise<ForgeDocRecord<D>> {
    const res = await raw("/forge/documents", {
      method: "POST",
      headers: { "Content-Type": "application/json", Accept: "application/json" },
      body: json({ localId, doc }),
    })
    if (res.status === 409) {
      const body = await conflictBody(res)
      if (body.code === "document_exists" && typeof body.id === "string") throw new DocumentExistsError(body.id)
    }
    if (!res.ok) throw await parseProblem(res)
    return (await res.json()) as ForgeDocRecord<D>
  },

  async update<D = unknown>(id: string, doc: D, baseRevision: number, clientId: string): Promise<ForgeUpdated> {
    const res = await raw(`/forge/documents/${enc(id)}`, {
      method: "PUT",
      headers: { "Content-Type": "application/json", Accept: "application/json", "X-Forge-Client": clientId },
      body: json({ doc, baseRevision }),
    })
    if (res.status === 409) {
      const body = await conflictBody(res)
      const cur = body.current as ForgeConflict<D> | undefined
      if (body.code === "revision_conflict" && cur) throw new RevisionConflictError<D>(cur)
    }
    if (!res.ok) throw await parseProblem(res)
    return (await res.json()) as ForgeUpdated
  },

  remove: (id: string) => api<void>(`/forge/documents/${enc(id)}`, { method: "DELETE" }),

  async putThumb(id: string, webp: Blob): Promise<void> {
    const res = await raw(`/forge/documents/${enc(id)}/thumb`, { method: "PUT", headers: { "Content-Type": "image/webp" }, body: webp })
    if (!res.ok) throw await parseProblem(res)
  },

  async uploadAsset(body: Blob | Uint8Array<ArrayBuffer>, mime: string): Promise<{ sha256: string; url: string }> {
    const res = await raw("/forge/assets", { method: "POST", headers: { "Content-Type": mime, Accept: "application/json" }, body })
    if (!res.ok) throw await parseProblem(res)
    return (await res.json()) as { sha256: string; url: string }
  },

  async fetchBlob(sha256: string): Promise<Uint8Array> {
    const res = await fetch(`/files/${enc(sha256)}`, { credentials: "include" })
    if (!res.ok) throw await parseProblem(res)
    return new Uint8Array(await res.arrayBuffer())
  },

  eventsUrl: (id: string) => `/api/v1/forge/documents/${enc(id)}/events`,
}
