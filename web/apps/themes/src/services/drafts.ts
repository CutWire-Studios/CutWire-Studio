import { api, ApiError, me } from "@cutwire/api-client"
import type { ThemeDoc } from "@cutwire/drift-theme"
import { createStore, del, entries, set } from "idb-keyval"

export interface Draft {
  id: string
  name: string
  doc: ThemeDoc
  updatedAt: number
}

const store = createStore("cutwire-themes", "drafts")

export async function saveDraftLocal(draft: Draft): Promise<void> {
  await set(draft.id, draft, store)
}

export async function listDraftsLocal(): Promise<Draft[]> {
  const all = await entries<string, Draft>(store)
  return all.map(([, d]) => d).sort((a, b) => b.updatedAt - a.updatedAt)
}

export async function deleteDraftLocal(id: string): Promise<void> {
  await del(id, store)
}

/*
 * Remote seam. The /api/v1/themes/drafts endpoint does not exist yet, so everything here must stay
 * quiet when it is missing: signed out, 404/405/501, or the network is down.
 */
const ABSENT = new Set([404, 405, 501])
let signedIn: { at: number; value: boolean } | null = null
let endpointMissing = false

async function isSignedIn(): Promise<boolean> {
  if (signedIn && Date.now() - signedIn.at < 60_000) return signedIn.value
  let value = false
  try {
    value = (await me()) !== null
  } catch {
    value = false
  }
  signedIn = { at: Date.now(), value }
  return value
}

/** Test hook: forget what was learned about the session and the endpoint. */
export function resetRemoteState() {
  signedIn = null
  endpointMissing = false
}

async function remote<T>(path: string, init?: RequestInit): Promise<T | null> {
  if (endpointMissing || !(await isSignedIn())) return null
  try {
    return await api<T>(path, init)
  } catch (e) {
    if (e instanceof ApiError && ABSENT.has(e.status)) endpointMissing = true
    return null
  }
}

/** Does nothing while `me()` is null; otherwise PUTs the draft. A missing endpoint is not an error. */
export async function saveDraftRemote(draft: Draft): Promise<void> {
  await remote<void>(`/themes/drafts/${encodeURIComponent(draft.id)}`, { method: "PUT", body: JSON.stringify(draft) })
}

export async function loadDraftsRemote(): Promise<Draft[]> {
  const res = await remote<Draft[] | { items: Draft[] }>("/themes/drafts")
  if (!res) return []
  return Array.isArray(res) ? res : (res.items ?? [])
}

export async function deleteDraftRemote(id: string): Promise<void> {
  await remote<void>(`/themes/drafts/${encodeURIComponent(id)}`, { method: "DELETE" })
}

/** Local and remote drafts merged by id, newest first. */
export async function listDrafts(): Promise<Draft[]> {
  const [local, remoteDrafts] = await Promise.all([listDraftsLocal(), loadDraftsRemote()])
  const byId = new Map<string, Draft>()
  for (const d of [...local, ...remoteDrafts]) {
    const prev = byId.get(d.id)
    if (!prev || d.updatedAt > prev.updatedAt) byId.set(d.id, d)
  }
  return [...byId.values()].sort((a, b) => b.updatedAt - a.updatedAt)
}

export async function saveDraft(draft: Draft): Promise<void> {
  await saveDraftLocal(draft)
  await saveDraftRemote(draft)
}

export async function deleteDraft(id: string): Promise<void> {
  await deleteDraftLocal(id)
  await deleteDraftRemote(id)
}
