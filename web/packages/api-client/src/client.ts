export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly code: string,
    readonly title: string,
    readonly detail?: string,
    readonly errors?: Record<string, string[]>,
  ) {
    super(detail ?? title)
    this.name = "ApiError"
  }
}

export interface Me {
  id: string
  userName: string
  displayName?: string
  avatarUrl?: string
}

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
  )
}

export async function api<T>(path: string, init: RequestInit = {}): Promise<T> {
  const headers = new Headers(init.headers)
  headers.set("X-Requested-With", "cutwire")
  headers.set("Accept", "application/json")
  if (init.body && !headers.has("Content-Type")) headers.set("Content-Type", "application/json")
  const res = await fetch(`/api/v1${path}`, { ...init, headers, credentials: "include" })
  if (!res.ok) throw await parseProblem(res)
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
