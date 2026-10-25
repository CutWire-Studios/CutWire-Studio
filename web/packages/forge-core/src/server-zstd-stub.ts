// The wasm zstd library can't run in Jint; the backend compresses and decompresses itself. Never called on the server.
const unsupported = (): never => {
  throw new Error("zstd is handled by the host")
}
export const zstdCompress = unsupported
export const zstdDecompress = unsupported
