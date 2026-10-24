import { describe, expect, it } from "vitest"
import { ANALYSIS_SIZE, analyseAudioFrame, TEXTURE_WIDTH } from "@/services/preview/audioAnalysis"

const RATE = 48000

/** Hann-windowed spectrum in dBFS, one value per linear bin, as an AnalyserNode would report it. */
function spectrumDb(wave: Float32Array): Float32Array {
  const n = wave.length
  const out = new Float32Array(n / 2)
  for (let k = 0; k < n / 2; k++) {
    let re = 0
    let im = 0
    for (let t = 0; t < n; t++) {
      const v = wave[t] * (0.5 - 0.5 * Math.cos((2 * Math.PI * t) / n))
      re += v * Math.cos((2 * Math.PI * k * t) / n)
      im -= v * Math.sin((2 * Math.PI * k * t) / n)
    }
    out[k] = 20 * Math.log10(Math.hypot(re, im) / n + 1e-12)
  }
  return out
}

const row = (pixels: Uint8Array, r: number) => pixels.subarray(r * TEXTURE_WIDTH * 4, (r + 1) * TEXTURE_WIDTH * 4)
const red = (pixels: Uint8Array, r: number) => Array.from({ length: TEXTURE_WIDTH }, (_, x) => row(pixels, r)[x * 4])

describe("analyseAudioFrame", () => {
  it("puts a 1 kHz sine in the log bin that covers 1 kHz", () => {
    const wave = new Float32Array(ANALYSIS_SIZE)
    for (let t = 0; t < wave.length; t++) wave[t] = 0.5 * Math.sin((2 * Math.PI * 1000 * t) / RATE)
    const frame = analyseAudioFrame(wave, spectrumDb(wave), RATE)
    const spectrum = red(frame.pixels, 0)
    const peak = spectrum.indexOf(Math.max(...spectrum))
    // log2-spaced bins from 20 Hz to 20 kHz: 1 kHz falls in bin 289.
    expect(Math.abs(peak - 289)).toBeLessThanOrEqual(1)
    expect(frame.pixels[3]).toBe(255)
    expect(frame.level).toBeCloseTo(0.85, 2)
  })

  it("maps the waveform from -1..1 to 0..1 in row 1", () => {
    const wave = new Float32Array(ANALYSIS_SIZE).fill(1)
    const frame = analyseAudioFrame(wave, new Float32Array(ANALYSIS_SIZE / 2).fill(-Infinity), RATE)
    expect(red(frame.pixels, 1).every((v) => v === 255)).toBe(true)
  })

  it("gives zero level, bass and spectrum for silence", () => {
    const wave = new Float32Array(ANALYSIS_SIZE)
    const frame = analyseAudioFrame(wave, new Float32Array(ANALYSIS_SIZE / 2).fill(-Infinity), RATE)
    expect(frame.level).toBe(0)
    expect(frame.bass).toBe(0)
    expect(red(frame.pixels, 0).every((v) => v === 0)).toBe(true)
    expect(red(frame.pixels, 1).every((v) => v === 128)).toBe(true)
  })
})
