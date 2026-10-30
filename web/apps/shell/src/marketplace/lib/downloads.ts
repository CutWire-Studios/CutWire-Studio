import { ApiError, marketplace, type InstallFile, type InstallJob, type MarketItem } from "@cutwire/api-client"
import { useSyncExternalStore } from "react"
import { triggerDownload } from "../hooks"
import { retryText } from "./format"
import { isTerminal, pollJob } from "./job"

export interface DownloadEntry {
  id: string
  itemKey: string
  title: string
  thumbUrl: string
  status: "starting" | "queued" | "processing" | "ready" | "failed"
  progress: number
  files: InstallFile[]
  error: string | null
}

let entries: DownloadEntry[] = []
const listeners = new Set<() => void>()

function update(id: string, patch: Partial<DownloadEntry>) {
  entries = entries.map((e) => (e.id === id ? { ...e, ...patch } : e))
  listeners.forEach((l) => l())
}

export const itemKeyOf = (i: Pick<MarketItem, "source" | "id">) => `${i.source}/${i.id}`

export function describeFailure(e: unknown): string {
  if (e instanceof ApiError && e.status === 429) return `Download limit reached. Try again ${retryText(e.retryAfter)}.`
  return e instanceof Error ? e.message : "The download failed."
}

/** Maps an install job onto the entry fields it controls. */
export function jobPatch(job: InstallJob): Partial<DownloadEntry> {
  return {
    status: job.status,
    progress: job.progress,
    files: job.files ?? [],
    error: job.status === "failed" ? (job.error?.detail ?? "The download failed.") : null,
  }
}

function deliver(files: InstallFile[]) {
  files.forEach((f, i) => setTimeout(() => triggerDownload(f.url, f.name), i * 400))
}

/** Starts an install and, once its files are ready, hands them to the browser. Ready-made installs download at once. */
export function startDownload(item: MarketItem, variantId?: string): string {
  const id = crypto.randomUUID()
  entries = [
    { id, itemKey: itemKeyOf(item), title: item.title, thumbUrl: item.thumbUrl, status: "starting", progress: 0, files: [], error: null },
    ...entries,
  ]
  listeners.forEach((l) => l())
  const finish = (job: InstallJob) => {
    update(id, jobPatch(job))
    if (job.status === "ready") deliver(job.files ?? [])
  }
  marketplace.install(item.source, item.id, variantId).then(
    (job) => {
      if (isTerminal(job)) return finish(job)
      update(id, jobPatch(job))
      pollJob({
        fetch: () => marketplace.getInstall(job.id),
        done: isTerminal,
        onUpdate: (j) => (isTerminal(j) ? finish(j) : update(id, jobPatch(j))),
        onError: (e) => update(id, { status: "failed", error: describeFailure(e) }),
      })
    },
    (e) => update(id, { status: "failed", error: describeFailure(e) }),
  )
  return id
}

export function clearFinished() {
  entries = entries.filter((e) => e.status !== "ready" && e.status !== "failed")
  listeners.forEach((l) => l())
}

const subscribe = (l: () => void) => {
  listeners.add(l)
  return () => listeners.delete(l)
}

export const useDownloads = () => useSyncExternalStore(subscribe, () => entries)
