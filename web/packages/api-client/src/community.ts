import { api, type Schemas } from "./client"

type Json = object

const send = <T = void>(method: string, path: string, body?: Json) =>
  api<T>(path, { method, body: body === undefined ? undefined : JSON.stringify(body) })

const qs = (params: Record<string, string | number | undefined | null>) => {
  const u = new URLSearchParams()
  for (const [k, v] of Object.entries(params)) if (v !== undefined && v !== null && v !== "") u.set(k, String(v))
  const s = u.toString()
  return s ? `?${s}` : ""
}

/** Numbers are typed `number | string` in the generated schema (int64 may arrive as strings). */
export const num = (v: number | string | null | undefined): number => (v === null || v === undefined ? 0 : typeof v === "number" ? v : Number(v))

export type VoteValue = 1 | -1
export type ItemStatus = "draft" | "published" | "hidden" | "removed"
export type MediaRule = Schemas["MediaRuleDto"]
export type KindInfo = Schemas["KindRulesDto"]
export type OwnerInfo = Schemas["OwnerDto"]
export type ItemVersion = Schemas["ItemVersionDto"]
export type ItemMedia = Schemas["ItemMediaDto"]
export type QualityRulesJson = Schemas["QualityRulesDto"]

/** Fields marked optional are part of the vote / preview contract that openapi.json may not carry yet. */
export type ItemCard = Schemas["ItemCardDto"] & { viewerVote?: VoteValue | null; previewStripUrl?: string | null; visibilityReason?: string | null }
export type ItemDetail = Omit<Schemas["ItemDetailDto"], "viewerVote"> & { viewerVote?: VoteValue | null; purgeAt?: string | null }
export type ItemPage = { items: ItemCard[]; nextCursor: string | null }
export type UserProfile = Omit<Schemas["UserProfileDto"], "items"> & { items: ItemPage }

export type ItemSort = "top" | "trending" | "new" | "downloads"
export interface ListParams {
  kind?: string
  q?: string
  tags?: string[]
  category?: string
  sort?: string
  owner?: string
  status?: string
  cursor?: string | null
  limit?: number
  /** Not in openapi.json yet: restricts "top" to a window such as `month`. */
  period?: string
}

export interface ItemInput {
  kind?: string
  title: string
  summary: string
  description: string
  category: string
  tags: string[]
  license: string
}

export const REPORT_REASONS = ["spam", "low_quality", "broken", "offensive", "copyright", "malware", "other"] as const
export type ReportReason = (typeof REPORT_REASONS)[number]

export type ModQueueType = "reported" | "hidden" | "flagged"
export type ModAction = "restore" | "remove" | "schedule-purge"

export interface ReportSummary {
  id: string
  reason: ReportReason
  note?: string | null
  reporter?: string | null
  createdAt: string
}
export interface FlagInfo {
  id: string
  code: string
  detail?: string | null
}
export interface ModQueueEntry {
  item: ItemCard
  reports: ReportSummary[]
  flags: FlagInfo[]
  visibilityReason?: string | null
  purgeAt?: string | null
}
export interface ModQueuePage {
  entries: ModQueueEntry[]
  nextCursor: string | null
}
export interface ModUser {
  id: string
  handle: string
  displayName: string
  avatarUrl: string | null
  email?: string
  roles: string[]
  trustLevel: string
  banned: boolean
  bannedUntil?: string | null
  itemCount?: number
}
export interface AuditEntry {
  id: string
  at: string
  actor: string | null
  action: string
  targetType: string
  target: string
  reason?: string | null
  auto: boolean
}
export interface AuditPage {
  entries: AuditEntry[]
  nextCursor: string | null
}

const enc = encodeURIComponent

export const community = {
  kinds: () => api<KindInfo[]>("/meta/kinds"),
  qualityRules: () => api<QualityRulesJson>("/meta/quality-rules"),
  /** Raw Bloom filter bytes (immutable, cached by URL). */
  bloom: async (url: string): Promise<Uint8Array> => {
    const res = await fetch(url, { credentials: "same-origin" })
    if (!res.ok) throw new Error("Could not load the English word list.")
    return new Uint8Array(await res.arrayBuffer())
  },

  list: (p: ListParams = {}) =>
    api<ItemPage>(`/items${qs({ ...p, tags: p.tags?.join(",") })}`),
  getBySlug: (kind: string, slug: string) => api<ItemDetail>(`/items/${enc(kind)}/${enc(slug)}`),
  getById: (id: string) => api<ItemDetail>(`/items/${enc(id)}`),
  user: (handle: string, p: { sort?: string; cursor?: string | null; limit?: number } = {}) =>
    api<UserProfile>(`/users/${enc(handle)}${qs(p)}`),
  downloadUrl: (id: string, version?: string) => `/api/v1/items/${enc(id)}/download${qs({ version })}`,

  create: (b: ItemInput & { kind: string }) => send<ItemDetail>("POST", "/items", b),
  update: (id: string, b: Partial<ItemInput>) => send<ItemDetail>("PATCH", `/items/${enc(id)}`, b),
  remove: (id: string) => send("DELETE", `/items/${enc(id)}`),
  uploadPackage: (id: string, file: Blob) =>
    api<ItemDetail>(`/items/${enc(id)}/package`, { method: "PUT", body: file, headers: { "Content-Type": "application/octet-stream" } }),
  uploadMedia: (id: string, role: string, file: Blob) =>
    api<ItemDetail>(`/items/${enc(id)}/media/${enc(role)}`, { method: "PUT", body: file, headers: { "Content-Type": "image/webp" } }),
  publish: (id: string) => send<ItemDetail>("POST", `/items/${enc(id)}/publish`),
  uploadVersion: (id: string, file: Blob, changelog?: string) =>
    api<ItemDetail>(`/items/${enc(id)}/versions${qs({ changelog })}`, {
      method: "POST",
      body: file,
      headers: { "Content-Type": "application/octet-stream" },
    }),

  // --- Contract not yet in openapi.json -------------------------------------------------
  vote: (id: string, value: VoteValue) => send("PUT", `/items/${enc(id)}/vote`, { value }),
  clearVote: (id: string) => send("DELETE", `/items/${enc(id)}/vote`),
  report: (id: string, reason: ReportReason, note?: string) => send("POST", `/items/${enc(id)}/reports`, { reason, note: note || undefined }),
  myItems: (p: { status?: string; cursor?: string | null } = {}) => api<ItemPage>(`/me/items${qs(p)}`),

  mod: {
    queue: (type: ModQueueType, cursor?: string | null) => api<ModQueuePage>(`/mod/queue${qs({ type, cursor })}`),
    itemAction: (id: string, action: ModAction, reason?: string) => send("POST", `/mod/items/${enc(id)}/${action}`, { reason }),
    bulk: (ids: string[], action: ModAction, reason: string) => send("POST", "/mod/items/bulk", { ids, action, reason }),
    dismissReport: (id: string, reason?: string) => send("POST", `/mod/reports/${enc(id)}/dismiss`, { reason }),
    upholdReport: (id: string, reason?: string) => send("POST", `/mod/reports/${enc(id)}/uphold`, { reason }),
    resolveFlag: (id: string, reason?: string) => send("POST", `/mod/flags/${enc(id)}/resolve`, { reason }),
    users: (q: string) => api<{ users: ModUser[]; nextCursor: string | null }>(`/mod/users${qs({ q })}`).then((p) => p.users),
    ban: (id: string, b: { reason: string; until?: string | null }) => send("POST", `/mod/users/${enc(id)}/ban`, b),
    unban: (id: string, reason?: string) => send("POST", `/mod/users/${enc(id)}/unban`, { reason }),
    trust: (id: string, level: string, reason?: string) => send("POST", `/mod/users/${enc(id)}/trust`, { level, reason }),
    setRoles: (id: string, roles: string[], reason?: string) => send("POST", `/mod/users/${enc(id)}/roles`, { roles, reason }),
    audit: (cursor?: string | null) => api<AuditPage>(`/mod/audit${qs({ cursor })}`),
  },
}
