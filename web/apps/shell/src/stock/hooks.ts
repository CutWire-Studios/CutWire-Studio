import { ApiError, stock, type DownloadJob, type ResolveJob } from "@cutwire/api-client"
import { useCallback, useEffect, useRef, useState, useSyncExternalStore } from "react"
import { audioReducer, idleAudio, type AudioAction, type AudioState } from "./lib/audio"
import { isTerminal, pollJob } from "./lib/job"

let catalog: ReturnType<typeof stock.catalog> | null = null
export const loadCatalog = () => {
  catalog ??= stock.catalog()
  catalog.catch(() => (catalog = null))
  return catalog
}

/* Shared audio: one element for the whole app, so starting a sound stops the previous one. */
let audio: HTMLAudioElement | null = null
let audioState: AudioState = idleAudio
const listeners = new Set<() => void>()

function dispatch(a: AudioAction) {
  const next = audioReducer(audioState, a)
  if (next === audioState) return
  audioState = next
  listeners.forEach((l) => l())
}

function element(): HTMLAudioElement {
  if (audio) return audio
  const el = new Audio()
  el.preload = "none"
  el.addEventListener("playing", () => dispatch({ type: "playing" }))
  el.addEventListener("waiting", () => dispatch({ type: "waiting" }))
  el.addEventListener("pause", () => !el.ended && dispatch({ type: "paused" }))
  el.addEventListener("ended", () => dispatch({ type: "ended" }))
  el.addEventListener("error", () => dispatch({ type: "error" }))
  el.addEventListener("timeupdate", () => el.duration > 0 && dispatch({ type: "progress", value: el.currentTime / el.duration }))
  return (audio = el)
}

export const sharedAudio = {
  toggle(id: string, src: string) {
    const el = element()
    const same = audioState.currentId === id
    dispatch({ type: "toggle", id })
    if (same && audioState.phase === "paused") return el.pause()
    if (!same) el.src = src
    el.play().catch(() => dispatch({ type: "error" }))
  },
  stop() {
    audio?.pause()
    dispatch({ type: "stop" })
  },
}

export function useSharedAudio(): AudioState {
  const subscribe = useCallback((l: () => void) => {
    listeners.add(l)
    return () => listeners.delete(l)
  }, [])
  return useSyncExternalStore(subscribe, () => audioState)
}

export function useStopAudioOnLeave() {
  useEffect(() => () => sharedAudio.stop(), [])
}

/** Starts and polls a job. `start` kicks it off, `fetch` re-reads it by id. */
function useJob<T extends { id: string; status: string }>(fetch: (id: string) => Promise<T>) {
  const [job, setJob] = useState<T | null>(null)
  const [error, setError] = useState<Error | null>(null)
  const [busy, setBusy] = useState(false)
  const cancel = useRef<(() => void) | null>(null)
  const fetchRef = useRef(fetch)
  fetchRef.current = fetch

  useEffect(() => () => cancel.current?.(), [])

  const start = useCallback((create: () => Promise<T>) => {
    cancel.current?.()
    setError(null)
    setJob(null)
    setBusy(true)
    create().then(
      (j) => {
        setJob(j)
        if (isTerminal(j)) return setBusy(false)
        cancel.current = pollJob({
          fetch: () => fetchRef.current(j.id),
          done: isTerminal,
          onUpdate: (v) => {
            setJob(v)
            if (isTerminal(v)) setBusy(false)
          },
          onError: (e) => {
            setError(e)
            setBusy(false)
          },
        })
      },
      (e: Error) => {
        setError(e)
        setBusy(false)
      },
    )
  }, [])

  const reset = useCallback(() => {
    cancel.current?.()
    setJob(null)
    setError(null)
    setBusy(false)
  }, [])

  return { job, error, busy, start, reset }
}

export function useResolveJob() {
  const j = useJob<ResolveJob>(stock.resolveJob)
  return { ...j, resolve: (url: string) => j.start(() => stock.resolve(url)) }
}

export function triggerDownload(url: string, filename?: string) {
  const a = document.createElement("a")
  a.href = url
  if (filename) a.download = filename
  a.rel = "noopener"
  document.body.appendChild(a)
  a.click()
  a.remove()
}

export function useDownloadJob() {
  const j = useJob<DownloadJob>(stock.downloadJob)
  const fired = useRef<string | null>(null)
  useEffect(() => {
    if (j.job?.status === "ready" && j.job.file && fired.current !== j.job.id) {
      fired.current = j.job.id
      triggerDownload(j.job.file.url, j.job.file.filename)
    }
  }, [j.job])
  return { ...j, download: (itemId: string, variantId?: string) => j.start(() => stock.download(itemId, variantId)) }
}

export function quotaRetry(e: Error | null): { exceeded: boolean; retryAfter?: number } {
  return e instanceof ApiError && e.status === 429 ? { exceeded: true, retryAfter: e.retryAfter } : { exceeded: false }
}
