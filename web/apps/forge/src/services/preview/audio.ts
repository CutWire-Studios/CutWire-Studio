import { publicUrl } from "@/shared/publicUrl"
import { ANALYSIS_SIZE, analyseAudioFrame, type AudioFrame } from "./audioAnalysis"

export interface AudioItem {
  id: string
  label: string
  url: string
  user?: boolean
}

export const AUDIO_SAMPLES: AudioItem[] = [{ id: "beat", label: "Beat loop (120 BPM)", url: publicUrl("samples/beat-120.mp3") }]

/** Onsets fire when bass jumps above its running average; the pulse then decays over 90 ms. */
const ONSET_GAP_S = 0.18
const PULSE_S = 0.09

let context: AudioContext | null = null
const sharedContext = () => (context ??= new AudioContext())

/**
 * Plays one music sample and analyses it for the shared audio contract. Drift derives these from the
 * timeline; the preview follows the element's playback, and beats come from an onset follower.
 */
export class AudioFollower {
  private ctx: AudioContext | null = null
  private el: HTMLAudioElement | null = null
  private analyser: AnalyserNode | null = null
  private gain: GainNode | null = null
  private wave = new Float32Array(ANALYSIS_SIZE)
  private freq = new Float32Array(ANALYSIS_SIZE / 2)
  private silentWave = new Float32Array(ANALYSIS_SIZE)
  private silentFreq = new Float32Array(ANALYSIS_SIZE / 2).fill(-Infinity)
  private bassAvg = 0
  private lastOnset = -Infinity
  item: AudioItem | null = null
  audible = false

  async load(item: AudioItem) {
    this.item = item
    if (!this.ctx) {
      this.ctx = sharedContext()
      this.analyser = this.ctx.createAnalyser()
      this.analyser.fftSize = ANALYSIS_SIZE
      this.analyser.smoothingTimeConstant = 0
      this.gain = this.ctx.createGain()
      this.gain.gain.value = this.audible ? 1 : 0
      this.analyser.connect(this.gain).connect(this.ctx.destination)
    }
    this.el?.pause()
    const el = new Audio()
    el.crossOrigin = "anonymous"
    el.loop = true
    el.src = item.url
    this.ctx.createMediaElementSource(el).connect(this.analyser!)
    this.el = el
  }

  setAudible(on: boolean) {
    this.audible = on
    if (this.gain) this.gain.gain.value = on ? 1 : 0
  }

  async play() {
    if (!this.el || !this.ctx) return
    await this.ctx.resume()
    await this.el.play().catch(() => {})
  }

  pause() {
    this.el?.pause()
  }

  get active(): boolean {
    return !!this.el && !this.el.paused
  }

  /** The window at `now` (seconds) and its scalars; silence while paused. */
  analyse(now: number): AudioFrame & { beat: number } {
    let frame: AudioFrame
    if (this.analyser && this.ctx && this.active) {
      this.analyser.getFloatTimeDomainData(this.wave)
      this.analyser.getFloatFrequencyData(this.freq)
      frame = analyseAudioFrame(this.wave, this.freq, this.ctx.sampleRate)
      this.bassAvg += (frame.bass - this.bassAvg) * 0.02
      if (frame.bass > this.bassAvg * 1.25 + 0.05 && now - this.lastOnset > ONSET_GAP_S) this.lastOnset = now
    } else {
      frame = analyseAudioFrame(this.silentWave, this.silentFreq, 48000)
    }
    return { ...frame, beat: Math.exp(-(now - this.lastOnset) / PULSE_S) }
  }
}

/** One follower per audio parameter, each on its own music; created on first use with the default loop. */
export class AudioFollowers {
  private byParam = new Map<string, AudioFollower>()
  private playing = true

  setPlaying(p: boolean) {
    this.playing = p
  }

  get(id: string): AudioFollower {
    const existing = this.byParam.get(id)
    if (existing) return existing
    const created = new AudioFollower()
    this.byParam.set(id, created)
    void created.load(AUDIO_SAMPLES[0]).then(() => {
      if (this.playing) void created.play()
    })
    return created
  }

  /** Stops and drops the followers of parameters not in `ids`. */
  retain(ids: Set<string>) {
    for (const [id, f] of this.byParam) {
      if (ids.has(id)) continue
      f.pause()
      this.byParam.delete(id)
    }
  }

  all(): AudioFollower[] {
    return [...this.byParam.values()]
  }
}
