// The shared audio contract (Drift docs/gpu-effects.md): a 2048-sample window, a 512x2 texture of
// spectrum (row 0) and waveform (row 1), and the level and bass scalars.
export const ANALYSIS_SIZE = 2048
export const TEXTURE_WIDTH = 512

const MIN_HZ = 20
const MAX_HZ = 20000
const BASS_HZ = 150

export interface AudioFrame {
  /** TEXTURE_WIDTH x 2 RGBA8, row 0 first. Values sit in R; G and B repeat it and A is 255. */
  pixels: Uint8Array
  level: number
  bass: number
}

const unit = (v: number) => Math.min(1, Math.max(0, v))
const toByte = (v: number) => Math.round(unit(v) * 255)

function writeRow(pixels: Uint8Array, row: number, values: Float32Array) {
  for (let x = 0; x < TEXTURE_WIDTH; x++) {
    const o = (row * TEXTURE_WIDTH + x) * 4
    const b = toByte(values[x])
    pixels[o] = b
    pixels[o + 1] = b
    pixels[o + 2] = b
    pixels[o + 3] = 255
  }
}

/** Max dBFS of the linear bins each of the 512 log bins covers; nearest linear bin if it covers none. */
function logSpectrum(freqDb: Float32Array, sampleRate: number): Float32Array {
  const bins = new Float32Array(TEXTURE_WIDTH).fill(-Infinity)
  const logOf = (hz: number) => Math.floor((Math.log(hz / MIN_HZ) / Math.log(MAX_HZ / MIN_HZ)) * TEXTURE_WIDTH)
  for (let i = 0; i < freqDb.length; i++) {
    const hz = (i * sampleRate) / ANALYSIS_SIZE
    if (hz < MIN_HZ || hz >= MAX_HZ) continue
    const k = Math.min(TEXTURE_WIDTH - 1, logOf(hz))
    if (freqDb[i] > bins[k]) bins[k] = freqDb[i]
  }
  for (let k = 0; k < TEXTURE_WIDTH; k++) {
    if (bins[k] !== -Infinity) continue
    const centre = MIN_HZ * Math.pow(MAX_HZ / MIN_HZ, (k + 0.5) / TEXTURE_WIDTH)
    const i = Math.min(freqDb.length - 1, Math.round((centre * ANALYSIS_SIZE) / sampleRate))
    bins[k] = freqDb[i]
  }
  return bins
}

/**
 * Analyses one window. `wave` is 2048 mono samples in -1..1; `freqDb` is the spectrum of the same
 * window in dBFS, one value per linear bin (AnalyserNode.getFloatFrequencyData at fftSize 2048).
 */
export function analyseAudioFrame(wave: Float32Array, freqDb: Float32Array, sampleRate: number): AudioFrame {
  const pixels = new Uint8Array(TEXTURE_WIDTH * 2 * 4)
  const spectrum = logSpectrum(freqDb, sampleRate)
  for (let k = 0; k < TEXTURE_WIDTH; k++) spectrum[k] = (spectrum[k] + 90) / 80
  writeRow(pixels, 0, spectrum)

  const decimated = new Float32Array(TEXTURE_WIDTH)
  const group = wave.length / TEXTURE_WIDTH
  let power = 0
  for (let x = 0; x < TEXTURE_WIDTH; x++) {
    let sum = 0
    for (let j = 0; j < group; j++) sum += wave[x * group + j]
    decimated[x] = (sum / group + 1) / 2
  }
  writeRow(pixels, 1, decimated)
  for (const v of wave) power += v * v

  const levelDb = 10 * Math.log10(power / wave.length)
  let bassPower = 0
  let bassBins = 0
  for (let i = 0; i < freqDb.length; i++) {
    if ((i * sampleRate) / ANALYSIS_SIZE >= BASS_HZ) break
    bassPower += Math.pow(10, freqDb[i] / 10)
    bassBins++
  }
  const bassDb = 10 * Math.log10(bassPower / bassBins)

  return {
    pixels,
    level: unit((levelDb + 60) / 60),
    bass: unit((bassDb + 60) / 60),
  }
}
