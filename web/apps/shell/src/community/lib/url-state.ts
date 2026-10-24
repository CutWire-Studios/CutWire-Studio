export const SORTS = ["trending", "top", "new", "downloads"] as const
export type Sort = (typeof SORTS)[number]

export interface BrowseState {
  kind: string
  q: string
  tags: string[]
  category: string
  sort: Sort
}

export const DEFAULT_SORT: Sort = "trending"

export function parseBrowse(params: URLSearchParams): BrowseState {
  const sort = params.get("sort")
  const tags = (params.get("tags") ?? "")
    .split(",")
    .map((t) => t.trim())
    .filter(Boolean)
  return {
    kind: params.get("kind") ?? "",
    q: params.get("q") ?? "",
    tags: [...new Set(tags)],
    category: params.get("category") ?? "",
    sort: SORTS.includes(sort as Sort) ? (sort as Sort) : DEFAULT_SORT,
  }
}

/** Defaults and empty values are omitted so URLs stay short and equal states give equal URLs. */
export function toBrowseParams(state: Partial<BrowseState>): URLSearchParams {
  const p = new URLSearchParams()
  if (state.kind) p.set("kind", state.kind)
  if (state.q?.trim()) p.set("q", state.q.trim())
  if (state.tags?.length) p.set("tags", state.tags.join(","))
  if (state.category) p.set("category", state.category)
  if (state.sort && state.sort !== DEFAULT_SORT) p.set("sort", state.sort)
  return p
}

/** Changing kind invalidates category and tags, which come from the kind's vocabulary. */
export function patchBrowse(state: BrowseState, patch: Partial<BrowseState>): BrowseState {
  const next = { ...state, ...patch }
  if (patch.kind !== undefined && patch.kind !== state.kind) {
    if (patch.category === undefined) next.category = ""
    if (patch.tags === undefined) next.tags = []
  }
  return next
}

export function browseHref(state: Partial<BrowseState>): string {
  const qs = toBrowseParams(state).toString()
  return qs ? `/community/browse?${qs}` : "/community/browse"
}
