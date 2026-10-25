import { afterEach, describe, expect, it, vi } from "vitest"
import { audioReducer, idleAudio } from "./audio"
import { retryText } from "./format"
import { isTerminal, pollJob } from "./job"
import { parseSearch, patchSearch, searchHref, toSearchParams } from "./url-state"

describe("search url state", () => {
  it("round-trips filters and omits empties", () => {
    const s = parseSearch(new URLSearchParams("type=video&provider=pexels&q=sea&orientation=landscape&hd=1&empty="))
    expect(s).toEqual({ type: "video", provider: "pexels", q: "sea", filters: { orientation: "landscape", hd: "1" } })
    expect(parseSearch(toSearchParams(s))).toEqual(s)
    expect(searchHref({})).toBe("/stock/search")
    expect(searchHref({ type: "video", q: " a b " })).toBe("/stock/search?type=video&q=a+b")
  })
  it("drops filters when type or provider changes", () => {
    const s = parseSearch(new URLSearchParams("type=video&provider=pexels&hd=1"))
    expect(patchSearch(s, { type: "image" })).toMatchObject({ type: "image", provider: "", filters: {} })
    expect(patchSearch(s, { provider: "pixabay" }).filters).toEqual({})
    expect(patchSearch(s, { q: "x" }).filters).toEqual({ hd: "1" })
  })
})

describe("pollJob", () => {
  afterEach(() => vi.useRealTimers())
  it("polls until terminal, then stops", async () => {
    vi.useFakeTimers()
    const states = ["queued", "processing", "ready"]
    let n = 0
    const fetch = vi.fn(async () => ({ status: states[n++]! }))
    const seen: string[] = []
    pollJob({ fetch, done: isTerminal, onUpdate: (j) => seen.push(j.status), onError: () => {}, intervalMs: 1000 })
    await vi.advanceTimersByTimeAsync(0)
    await vi.advanceTimersByTimeAsync(1000)
    await vi.advanceTimersByTimeAsync(1000)
    await vi.advanceTimersByTimeAsync(10_000)
    expect(seen).toEqual(["queued", "processing", "ready"])
    expect(fetch).toHaveBeenCalledTimes(3)
  })
  it("stops on cancel and reports errors and timeouts", async () => {
    vi.useFakeTimers()
    const fetch = vi.fn(async () => ({ status: "queued" }))
    const onUpdate = vi.fn()
    const cancel = pollJob({ fetch, done: isTerminal, onUpdate, onError: () => {}, intervalMs: 1000 })
    await vi.advanceTimersByTimeAsync(0)
    cancel()
    await vi.advanceTimersByTimeAsync(5000)
    expect(fetch).toHaveBeenCalledTimes(1)

    const onError = vi.fn()
    pollJob({ fetch, done: isTerminal, onUpdate: () => {}, onError, intervalMs: 1000, timeoutMs: 3000 })
    await vi.advanceTimersByTimeAsync(10_000)
    expect(onError).toHaveBeenCalledTimes(1)

    const failing = vi.fn()
    pollJob({ fetch: async () => Promise.reject(new Error("x")), done: isTerminal, onUpdate: () => {}, onError: failing })
    await vi.advanceTimersByTimeAsync(0)
    expect(failing).toHaveBeenCalledTimes(1)
  })
})

describe("audio reducer", () => {
  it("switches sounds, toggles pause/resume and resets on error", () => {
    let s = audioReducer(idleAudio, { type: "toggle", id: "a" })
    expect(s).toEqual({ currentId: "a", phase: "loading", progress: 0 })
    s = audioReducer(s, { type: "playing" })
    s = audioReducer(s, { type: "progress", value: 0.5 })
    expect(audioReducer(s, { type: "toggle", id: "a" })).toMatchObject({ phase: "paused", progress: 0.5 })
    expect(audioReducer(audioReducer(s, { type: "toggle", id: "a" }), { type: "toggle", id: "a" }).phase).toBe("loading")
    expect(audioReducer(s, { type: "toggle", id: "b" })).toEqual({ currentId: "b", phase: "loading", progress: 0 })
    expect(audioReducer(s, { type: "ended" })).toMatchObject({ phase: "paused", progress: 0 })
    expect(audioReducer(s, { type: "error" })).toBe(idleAudio)
    expect(audioReducer(idleAudio, { type: "playing" })).toBe(idleAudio)
  })
})

describe("retryText", () => {
  it("scales units", () => {
    expect(retryText(30)).toBe("in 30 seconds")
    expect(retryText(600)).toBe("in 10 minutes")
    expect(retryText(7200)).toBe("in 2 hours")
    expect(retryText(undefined)).toBe("later")
  })
})
