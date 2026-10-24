const MAGIC = "CWBLOOM1";
const HEADER_SIZE = 24;

/** Reader for the CWBLOOM1 format; identical to `BloomFilter` in the backend (docs/quality-gates.md). */
export class BloomFilter {
  private constructor(
    private readonly bits: Uint8Array,
    readonly bitCount: number,
    readonly hashCount: number,
    readonly seed: number,
    readonly wordCount: number,
  ) {}

  static parse(file: Uint8Array): BloomFilter {
    if (file.length < HEADER_SIZE || String.fromCharCode(...file.subarray(0, 8)) !== MAGIC) {
      throw new Error("Not a CWBLOOM1 file.");
    }
    if (file[8] !== 1) throw new Error(`Unsupported Bloom filter version ${file[8]}.`);
    const view = new DataView(file.buffer, file.byteOffset, file.byteLength);
    const hashCount = file[9]!;
    const bitCount = view.getUint32(12, true);
    const seed = view.getUint32(16, true);
    const wordCount = view.getUint32(20, true);
    if (hashCount === 0 || bitCount === 0 || bitCount % 8 !== 0 || file.length !== HEADER_SIZE + bitCount / 8) {
      throw new Error("Bloom filter header is inconsistent with its length.");
    }
    return new BloomFilter(file.subarray(HEADER_SIZE), bitCount, hashCount, seed, wordCount);
  }

  mightContain(word: string): boolean {
    for (const position of bloomPositions(new TextEncoder().encode(word), this.bitCount, this.hashCount, this.seed)) {
      if ((this.bits[position >>> 3]! & (1 << (position & 7))) === 0) return false;
    }
    return true;
  }
}

export function fnv1a(data: Uint8Array, seed: number): number {
  let hash = (0x811c9dc5 ^ seed) >>> 0;
  for (const byte of data) {
    hash = Math.imul(hash ^ byte, 0x01000193) >>> 0;
  }
  return hash;
}

export function murmur3(data: Uint8Array, seed: number): number {
  const c1 = 0xcc9e2d51;
  const c2 = 0x1b873593;
  const rotl = (x: number, r: number) => ((x << r) | (x >>> (32 - r))) >>> 0;
  let h = seed >>> 0;
  const blocks = data.length >>> 2;
  for (let i = 0; i < blocks; i++) {
    const o = i * 4;
    let k = (data[o]! | (data[o + 1]! << 8) | (data[o + 2]! << 16) | (data[o + 3]! << 24)) >>> 0;
    k = Math.imul(k, c1) >>> 0;
    k = rotl(k, 15);
    k = Math.imul(k, c2) >>> 0;
    h = (h ^ k) >>> 0;
    h = rotl(h, 13);
    h = (Math.imul(h, 5) + 0xe6546b64) >>> 0;
  }
  const tail = blocks * 4;
  let t = 0;
  switch (data.length & 3) {
    case 3:
      t ^= data[tail + 2]! << 16;
    // falls through
    case 2:
      t ^= data[tail + 1]! << 8;
    // falls through
    case 1:
      t ^= data[tail]!;
      t = Math.imul(t >>> 0, c1) >>> 0;
      t = rotl(t, 15);
      t = Math.imul(t, c2) >>> 0;
      h = (h ^ t) >>> 0;
  }
  h = (h ^ data.length) >>> 0;
  h = (h ^ (h >>> 16)) >>> 0;
  h = Math.imul(h, 0x85ebca6b) >>> 0;
  h = (h ^ (h >>> 13)) >>> 0;
  h = Math.imul(h, 0xc2b2ae35) >>> 0;
  h = (h ^ (h >>> 16)) >>> 0;
  return h;
}

export function bloomHashes(word: Uint8Array, seed: number): { h1: number; h2: number } {
  return { h1: fnv1a(word, seed), h2: (murmur3(word, seed) | 1) >>> 0 };
}

/** position_i = (h1 + i * h2) mod m. The sum stays below 2^53, so doubles are exact. */
export function bloomPositions(word: Uint8Array, bitCount: number, hashCount: number, seed: number): number[] {
  const { h1, h2 } = bloomHashes(word, seed);
  return Array.from({ length: hashCount }, (_, i) => (h1 + i * h2) % bitCount);
}
