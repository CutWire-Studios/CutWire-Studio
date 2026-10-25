export function formatDuration(ms: number | null | undefined): string {
  if (ms == null) return ""
  const s = Math.round(ms / 1000)
  return `${Math.floor(s / 60)}:${String(s % 60).padStart(2, "0")}`
}

/** "in 5 minutes" style hint for a quota retry delay given in seconds. */
export function retryText(seconds: number | undefined): string {
  if (!seconds || seconds <= 0) return "later"
  if (seconds < 90) return `in ${Math.ceil(seconds)} seconds`
  if (seconds < 5400) return `in ${Math.ceil(seconds / 60)} minutes`
  return `in ${Math.ceil(seconds / 3600)} hours`
}

export function attributionText(i: { title: string; creator: { name: string } | null; license: { name: string; attribution: string | null } }): string {
  if (i.license.attribution) return i.license.attribution
  return `${i.title}${i.creator ? ` by ${i.creator.name}` : ""} (${i.license.name})`
}
