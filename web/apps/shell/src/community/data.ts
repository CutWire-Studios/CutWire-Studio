import { community, type ItemCard, type KindInfo, type QualityRulesJson } from "@cutwire/api-client"
import { BloomFilter, type QualityRules } from "@cutwire/content-rules"
import { useCallback, useEffect, useRef, useState } from "react"

let kindsPromise: Promise<KindInfo[]> | null = null
let rulesPromise: Promise<QualityRulesJson> | null = null
let bloomPromise: Promise<BloomFilter> | null = null

const once = <T>(slot: Promise<T> | null, load: () => Promise<T>, reset: () => void): Promise<T> => {
  const p = slot ?? load()
  p.catch(reset)
  return p
}

export const loadKinds = () => (kindsPromise = once(kindsPromise, community.kinds, () => (kindsPromise = null)))
export const loadRules = () => (rulesPromise = once(rulesPromise, community.qualityRules, () => (rulesPromise = null)))

/** The English word list is ~330 KB; only fetched when something needs to check prose. */
export const loadBloom = (rules: QualityRulesJson) =>
  (bloomPromise = once(bloomPromise, async () => BloomFilter.parse(await community.bloom(rules.englishWordList.url)), () => (bloomPromise = null)))

export interface Async<T> {
  data?: T
  error: Error | null
  loading: boolean
  reload: () => void
  set: (v: T) => void
}

export function useAsync<T>(load: () => Promise<T>, deps: unknown[]): Async<T> {
  const [state, setState] = useState<{ data?: T; error: Error | null; loading: boolean }>({ error: null, loading: true })
  const [tick, setTick] = useState(0)
  useEffect(() => {
    let live = true
    setState((s) => ({ data: s.data, error: null, loading: true }))
    load().then(
      (data) => live && setState({ data, error: null, loading: false }),
      (error: Error) => live && setState({ error, loading: false }),
    )
    return () => {
      live = false
    }
    // oxlint-disable-next-line react-hooks/exhaustive-deps
  }, [...deps, tick])
  const set = useCallback((data: T) => setState({ data, error: null, loading: false }), [])
  return { ...state, reload: () => setTick((t) => t + 1), set }
}

export const useKinds = () => useAsync(loadKinds, [])
export const useRules = () => useAsync(() => loadRules() as Promise<QualityRulesJson>, [])
export const asRules = (r: QualityRulesJson) => r as unknown as QualityRules

export function kindName(kinds: KindInfo[] | undefined, id: string): string {
  return kinds?.find((k) => k.id === id)?.displayName ?? id
}

export interface Infinite<T = ItemCard> {
  items: T[]
  loading: boolean
  error: Error | null
  hasMore: boolean
  loadMore: () => void
  sentinel: (el: Element | null) => void
  patch: (id: string, fn: (i: T) => T) => void
  reload: () => void
}

/** Cursor pagination. Restarts whenever `key` changes; `fetchPage` must only depend on what `key` encodes. */
export function useInfinite<T = ItemCard>(
  fetchPage: (cursor: string | null) => Promise<{ items: T[]; nextCursor: string | null }>,
  key: string,
  idOf: (t: T) => string = (t) => (t as { id: string }).id,
): Infinite<T> {
  const [items, setItems] = useState<T[]>([])
  const [cursor, setCursor] = useState<string | null>(null)
  const [hasMore, setHasMore] = useState(true)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<Error | null>(null)
  const [epoch, setEpoch] = useState(0)
  const busy = useRef(false)
  const fetchRef = useRef(fetchPage)
  fetchRef.current = fetchPage
  const gen = useRef(0)

  const idRef = useRef(idOf)
  idRef.current = idOf
  const run = useCallback((from: string | null, reset: boolean) => {
    if (busy.current) return
    busy.current = true
    const mine = gen.current
    setLoading(true)
    setError(null)
    fetchRef.current(from).then(
      (page) => {
        if (mine !== gen.current) return
        setItems((cur) => (reset ? page.items : [...cur, ...page.items.filter((n) => !cur.some((c) => idRef.current(c) === idRef.current(n)))]))
        setCursor(page.nextCursor)
        setHasMore(page.nextCursor !== null)
        busy.current = false
        setLoading(false)
      },
      (e: Error) => {
        if (mine !== gen.current) return
        setError(e)
        setHasMore(false)
        busy.current = false
        setLoading(false)
      },
    )
  }, [])

  useEffect(() => {
    gen.current++
    busy.current = false
    setItems([])
    setCursor(null)
    setHasMore(true)
    run(null, true)
  }, [key, epoch, run])

  const loadMore = useCallback(() => {
    if (hasMore && !busy.current) run(cursor, false)
  }, [hasMore, cursor, run])

  const observer = useRef<IntersectionObserver | null>(null)
  const moreRef = useRef(loadMore)
  moreRef.current = loadMore
  const sentinel = useCallback((el: Element | null) => {
    observer.current?.disconnect()
    observer.current = null
    if (!el || typeof IntersectionObserver === "undefined") return
    observer.current = new IntersectionObserver((e) => e[0]?.isIntersecting && moreRef.current(), { rootMargin: "600px" })
    observer.current.observe(el)
  }, [])

  const patch = useCallback((id: string, fn: (i: T) => T) => setItems((cur) => cur.map((i) => (idRef.current(i) === id ? fn(i) : i))), [])
  return { items, loading, error, hasMore, loadMore, sentinel, patch, reload: () => setEpoch((e) => e + 1) }
}
