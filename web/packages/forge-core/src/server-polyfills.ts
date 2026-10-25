// The server runs forge-core in Jint, a bare ECMAScript engine: no TextEncoder, atob or structuredClone.
// Only what forge-core's pure code paths touch is provided here.
const g = globalThis as Record<string, unknown>

if (typeof g.TextEncoder === "undefined") {
  g.TextEncoder = class {
    encode(s = ""): Uint8Array {
      const out: number[] = []
      for (let i = 0; i < s.length; i++) {
        let c = s.charCodeAt(i)
        if (c >= 0xd800 && c < 0xdc00 && i + 1 < s.length) {
          const d = s.charCodeAt(i + 1)
          if (d >= 0xdc00 && d < 0xe000) {
            c = 0x10000 + ((c - 0xd800) << 10) + (d - 0xdc00)
            i++
          }
        }
        if (c < 0x80) out.push(c)
        else if (c < 0x800) out.push(0xc0 | (c >> 6), 0x80 | (c & 63))
        else if (c < 0x10000) out.push(0xe0 | (c >> 12), 0x80 | ((c >> 6) & 63), 0x80 | (c & 63))
        else out.push(0xf0 | (c >> 18), 0x80 | ((c >> 12) & 63), 0x80 | ((c >> 6) & 63), 0x80 | (c & 63))
      }
      return Uint8Array.from(out)
    }
  }
}

if (typeof g.TextDecoder === "undefined") {
  g.TextDecoder = class {
    decode(b: Uint8Array = new Uint8Array(0)): string {
      let s = ""
      for (let i = 0; i < b.length; ) {
        const c = b[i++]!
        let cp = c
        if (c >= 0xf0) cp = ((c & 7) << 18) | ((b[i++]! & 63) << 12) | ((b[i++]! & 63) << 6) | (b[i++]! & 63)
        else if (c >= 0xe0) cp = ((c & 15) << 12) | ((b[i++]! & 63) << 6) | (b[i++]! & 63)
        else if (c >= 0xc0) cp = ((c & 31) << 6) | (b[i++]! & 63)
        s += String.fromCodePoint(cp)
      }
      return s
    }
  }
}

if (typeof g.structuredClone === "undefined") {
  g.structuredClone = (v: unknown) => (v === undefined ? v : JSON.parse(JSON.stringify(v)))
}

if (typeof g.console === "undefined") {
  const noop = () => {}
  g.console = { log: noop, info: noop, warn: noop, error: noop, debug: noop }
}
