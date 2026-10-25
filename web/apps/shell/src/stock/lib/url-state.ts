export interface SearchState {
  type: string
  provider: string
  q: string
  /** Provider filter values keyed by filter id. */
  filters: Record<string, string>
}

const RESERVED = new Set(["type", "provider", "q"])

export function parseSearch(params: URLSearchParams): SearchState {
  const filters: Record<string, string> = {}
  for (const [k, v] of params) if (!RESERVED.has(k) && v !== "") filters[k] = v
  return { type: params.get("type") ?? "", provider: params.get("provider") ?? "", q: params.get("q") ?? "", filters }
}

/** Empty values are omitted so equal states give equal URLs. Filter keys are sorted for stability. */
export function toSearchParams(state: Partial<SearchState>): URLSearchParams {
  const p = new URLSearchParams()
  if (state.type) p.set("type", state.type)
  if (state.provider) p.set("provider", state.provider)
  if (state.q?.trim()) p.set("q", state.q.trim())
  for (const k of Object.keys(state.filters ?? {}).sort()) {
    const v = state.filters![k]
    if (v && !RESERVED.has(k)) p.set(k, v)
  }
  return p
}

/** Filters belong to a provider, so switching type or provider drops them. */
export function patchSearch(state: SearchState, patch: Partial<SearchState>): SearchState {
  const next = { ...state, ...patch }
  if (patch.type !== undefined && patch.type !== state.type) {
    if (patch.provider === undefined) next.provider = ""
    if (patch.filters === undefined) next.filters = {}
  } else if (patch.provider !== undefined && patch.provider !== state.provider && patch.filters === undefined) {
    next.filters = {}
  }
  return next
}

export function searchHref(state: Partial<SearchState>): string {
  const qs = toSearchParams(state).toString()
  return qs ? `/stock/search?${qs}` : "/stock/search"
}
