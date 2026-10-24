import { isTrackParam } from "@cutwire/forge-core/doc/params"
import type { ForgeDoc } from "@cutwire/forge-core/doc/types"
import { paramSlot, usesClipMask } from "./bindings"
import { FrameCanvas, loadMedia, sample, type FaceTrack, type LoadedMedia, type MediaItem } from "./media"
import type { DriftRenderer, SourceSlot } from "./renderer"

/** The sample a track or matte parameter shows until it is changed. */
export const defaultSource = (type: "track" | "trackMask"): MediaItem => sample(type === "track" ? "city-pan" : "dancer-matte")

/**
 * Every texture input of the preview: the two clips, the clip mask, the depth map, one per track
 * parameter, and the face track of the clip. Videos stay frame-locked to the clip on slot 0.
 */
export class PreviewSources {
  private items = new Map<SourceSlot, MediaItem>([
    [0, sample("mountains-push")],
    [1, sample("city-pan")],
    ["mask", sample("dancer-matte")],
  ])
  private media = new Map<SourceSlot, LoadedMedia>()
  private frameA = new FrameCanvas()
  private frameB = new FrameCanvas()
  private frameC = new FrameCanvas()
  private face: FaceTrack | null = null
  private playing = true

  constructor(
    private renderer: DriftRenderer,
    private onChange: () => void,
  ) {}

  ids(): Record<string, string> {
    return Object.fromEntries([...this.items].map(([slot, item]) => [slot, item.id]))
  }

  itemOf(slot: SourceSlot): MediaItem | undefined {
    return this.items.get(slot)
  }

  has(slot: SourceSlot): boolean {
    return this.media.has(slot)
  }

  async set(slot: SourceSlot, item: MediaItem) {
    const before = this.items.get(slot)?.id
    this.items.set(slot, item)
    if (before !== item.id) this.onChange()
    if (slot === 0) this.attachSidecars(item)
    const prev = this.media.get(slot)
    const m = await loadMedia(item)
    if (this.items.get(slot) !== item) return
    if (prev && prev !== m && prev.el instanceof HTMLVideoElement && !this.isUsed(prev)) prev.el.pause()
    this.media.set(slot, m)
    if (m.el instanceof HTMLVideoElement && this.playing) m.el.play().catch(() => {})
  }

  /** Loads the default source of each track parameter the document has and no source yet. */
  syncTrackParams(doc: ForgeDoc) {
    for (const p of doc.params) {
      if (p.type !== "track" && p.type !== "trackMask") continue
      const slot = paramSlot(p.identifier)
      if (!this.items.has(slot)) void this.set(slot, defaultSource(p.type))
    }
  }

  /** Reloads every slot, as when the preview starts. */
  reload() {
    for (const [slot, item] of [...this.items]) void this.set(slot, item)
  }

  /** The matte, depth map and face track that belong to the clip on slot 0; absent ones are dropped. */
  private attachSidecars(item: MediaItem) {
    if (item.matte) void this.set("mask", sample(item.matte))
    if (item.depth) void this.set("depth", { id: `${item.id}-depth`, label: `${item.label} depth`, kind: "video", url: item.depth })
    else this.detach("depth")
    this.face = null
    if (item.face) {
      void fetch(item.face)
        .then((r) => r.json())
        .then((f: FaceTrack) => {
          if (this.items.get(0) === item) this.face = f
        })
        .catch(() => {})
    }
  }

  private detach(slot: SourceSlot) {
    const m = this.media.get(slot)
    this.items.delete(slot)
    this.media.delete(slot)
    if (m?.el instanceof HTMLVideoElement && !this.isUsed(m)) m.el.pause()
  }

  private isUsed(m: LoadedMedia) {
    return [...this.media.values()].includes(m)
  }

  private videos(): HTMLVideoElement[] {
    return [...this.media.values()].flatMap((m) => (m.el instanceof HTMLVideoElement ? [m.el] : []))
  }

  setPlaying(p: boolean) {
    this.playing = p
    for (const v of this.videos()) {
      if (p) v.play().catch(() => {})
      else v.pause()
    }
  }

  pauseAll() {
    for (const v of this.videos()) v.pause()
  }

  /** Clip 0 when it is a video; the clock follows it. */
  effectVideo(): HTMLVideoElement | null {
    const el = this.media.get(0)?.el
    return el instanceof HTMLVideoElement ? el : null
  }

  /** Uploads one clip at a size, cropped to fill; slot 1 is the "To" clip of transitions. */
  drawClip(slot: 0 | 1, w: number, h: number) {
    const m = this.media.get(slot)
    if (m) this.renderer.setSource(slot, (slot === 0 ? this.frameA : this.frameB).draw(m, w, h))
  }

  /** Uploads every source the document reads, at the frame size. */
  draw(doc: ForgeDoc, w: number, h: number, usesDepth: boolean) {
    const wanted: SourceSlot[] = [0]
    if (doc.kind === "transition") wanted.push(1)
    if (usesClipMask(doc)) wanted.push("mask")
    if (usesDepth) wanted.push("depth")
    for (const p of doc.params) if (isTrackParam(p)) wanted.push(paramSlot(p.identifier))
    for (const slot of wanted) {
      if (slot === 0 || slot === 1) {
        this.drawClip(slot, w, h)
        continue
      }
      const m = this.media.get(slot)
      if (!m) continue
      if (m.el instanceof HTMLVideoElement) {
        this.syncToClip(m.el)
        if (m.el.seeking || m.el.readyState < 2) continue
      }
      this.renderer.setSource(slot, this.frameC.draw(m, w, h))
    }
  }

  /**
   * A secondary video must stay frame-locked to the clip. Seeking every frame never settles (the clip
   * moves on while the video seeks) and a seeking video draws blank, so small drift is closed by
   * nudging the playback rate and only big jumps (the loop wrapping) seek.
   */
  private syncToClip(m: HTMLVideoElement) {
    const v = this.effectVideo()
    if (!v) return
    const drift = v.currentTime - m.currentTime
    if (!m.seeking && (Math.abs(drift) > 0.5 || (v.paused && Math.abs(drift) > 0.02))) m.currentTime = v.currentTime
    else m.playbackRate = 1 + Math.max(-0.5, Math.min(0.5, drift * 2))
  }

  /** The face uniforms for `time`: the frame's anchors, or the guard alone when there is no face. */
  faceUniforms(time: number): Record<string, number> {
    const f = this.face
    const frame = f && f.frames[Math.floor(time * f.fps) % f.frames.length]
    if (!frame?.valid) return { u_faceValid: 0 }
    const out: Record<string, number> = { u_faceValid: 1 }
    for (const [key, value] of Object.entries(frame)) {
      if (key !== "valid") out[`u_face${key[0].toUpperCase()}${key.slice(1)}`] = value as number
    }
    return out
  }
}
