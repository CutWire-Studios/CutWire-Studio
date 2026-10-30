export type AudioPhase = "idle" | "loading" | "playing" | "paused"
export interface AudioState {
  currentId: string
  phase: AudioPhase
  progress: number
}
export type AudioAction =
  | { type: "toggle"; id: string }
  | { type: "playing" }
  | { type: "waiting" }
  | { type: "paused" }
  | { type: "ended" }
  | { type: "error" }
  | { type: "progress"; value: number }
  | { type: "stop" }

export const idleAudio: AudioState = { currentId: "", phase: "idle", progress: 0 }

/** One sound at a time: toggling another id replaces the current one, toggling the same id pauses/resumes it. */
export function audioReducer(s: AudioState, a: AudioAction): AudioState {
  switch (a.type) {
    case "toggle":
      if (s.currentId !== a.id) return { currentId: a.id, phase: "loading", progress: 0 }
      return s.phase === "playing" || s.phase === "loading" ? { ...s, phase: "paused" } : { ...s, phase: "loading" }
    case "playing":
      return s.currentId ? { ...s, phase: "playing" } : s
    case "waiting":
      return s.phase === "playing" ? { ...s, phase: "loading" } : s
    case "paused":
      return s.phase === "idle" ? s : { ...s, phase: "paused" }
    case "ended":
      return { ...s, phase: "paused", progress: 0 }
    case "error":
    case "stop":
      return idleAudio
    case "progress":
      return { ...s, progress: a.value }
  }
}
