import type { components } from "../generated/schema"

export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly code: string,
    readonly title: string,
    readonly detail?: string,
    readonly errors?: Record<string, string[]>,
    readonly violations?: FieldViolation[],
    readonly retryAfter?: number,
  ) {
    super(detail ?? title)
    this.name = "ApiError"
  }
}

export type Schemas = components["schemas"]
export type Me = Schemas["MeDto"]
export type FieldViolation = Schemas["FieldViolation"]

export async function parseProblem(res: Response): Promise<ApiError> {
  let body: Record<string, unknown> = {}
  try {
    body = (await res.json()) as Record<string, unknown>
  } catch {
    // Non-JSON error bodies (proxies, 502s) fall back to the status text.
  }
  return new ApiError(
    res.status,
    typeof body.code === "string" ? body.code : "unknown",
    typeof body.title === "string" ? body.title : res.statusText || "Request failed",
    typeof body.detail === "string" ? body.detail : undefined,
    body.errors && typeof body.errors === "object" ? (body.errors as Record<string, string[]>) : undefined,
    Array.isArray(body.violations) ? (body.violations as FieldViolation[]) : undefined,
    typeof body.retryAfter === "number" ? body.retryAfter : Number(res.headers.get("Retry-After")) || undefined,
  )
}

const INSTALL_KEY = "studio-install-id"
let memoryInstallId: string | null = null

/** Anonymous quotas are keyed on this id, so it is generated once and kept in localStorage. */
export function installId(): string {
  try {
    const stored = localStorage.getItem(INSTALL_KEY)
    if (stored) return stored
    const id = crypto.randomUUID()
    localStorage.setItem(INSTALL_KEY, id)
    return id
  } catch {
    // Storage can be blocked; fall back to an id that lives as long as the page.
    return (memoryInstallId ??= crypto.randomUUID())
  }
}

export async function apiResponse(path: string, init: RequestInit = {}, accept: number[] = []): Promise<Response> {
  const headers = new Headers(init.headers)
  headers.set("X-Requested-With", "cutwire")
  headers.set("Accept", "application/json")
  headers.set("X-Studio-Install", installId())
  if (init.body && !headers.has("Content-Type")) headers.set("Content-Type", "application/json")
  const res = await fetch(`/api/v1${path}`, { ...init, headers, credentials: "include" })
  if (!res.ok && !accept.includes(res.status)) throw await parseProblem(res)
  return res
}

export async function api<T>(path: string, init: RequestInit = {}): Promise<T> {
  const res = await apiResponse(path, init)
  if (res.status === 204) return undefined as T
  return (await res.json()) as T
}

export async function me(): Promise<Me | null> {
  try {
    return await api<Me>("/me")
  } catch (e) {
    if (e instanceof ApiError && e.status === 401) return null
    throw e
  }
}
