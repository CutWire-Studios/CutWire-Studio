export interface PollOptions<T> {
  fetch: () => Promise<T>
  done: (v: T) => boolean
  onUpdate: (v: T) => void
  onError: (e: Error) => void
  intervalMs?: number
  /** Gives up (as an error) after this long without reaching a terminal state. */
  timeoutMs?: number
}

/** Polls until `done`, then stops. Returns a cancel function; a cancelled poller never calls back again. */
export function pollJob<T>(o: PollOptions<T>): () => void {
  const interval = o.intervalMs ?? 1000
  const timeout = o.timeoutMs ?? 5 * 60_000
  let cancelled = false
  let timer: ReturnType<typeof setTimeout> | undefined
  let elapsed = 0

  const tick = async () => {
    try {
      const v = await o.fetch()
      if (cancelled) return
      o.onUpdate(v)
      if (o.done(v)) return
    } catch (e) {
      if (!cancelled) o.onError(e as Error)
      return
    }
    elapsed += interval
    if (elapsed >= timeout) {
      o.onError(new Error("Timed out waiting for the job to finish"))
      return
    }
    timer = setTimeout(tick, interval)
  }
  timer = setTimeout(tick, 0)
  return () => {
    cancelled = true
    clearTimeout(timer)
  }
}

export const isTerminal = (j: { status: string }) => j.status === "ready" || j.status === "failed"
