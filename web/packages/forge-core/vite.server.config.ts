// One ES2020 IIFE with the pure parts of forge-core, for the backend's embedded JS engine (Jint).
// Regenerate with `pnpm --filter @cutwire/forge-core bundle:server`; the output is committed.
import { fileURLToPath } from "node:url"
import { defineConfig } from "vite"

const stubZstd = {
  name: "stub-zstd",
  enforce: "pre" as const,
  resolveId(source: string, importer?: string) {
    if (importer && /\/export\/(link|driftfx)\.ts$/.test(importer) && source === "./zstd") return fileURLToPath(new URL("./src/server-zstd-stub.ts", import.meta.url))
    return null
  },
}

export default defineConfig({
  plugins: [stubZstd],
  define: { "process.env.NODE_ENV": JSON.stringify("production") },
  build: {
    target: "es2020",
    lib: { entry: fileURLToPath(new URL("./src/server-entry.ts", import.meta.url)), formats: ["iife"], name: "ForgeServer", fileName: () => "forge-core.js" },
    outDir: fileURLToPath(new URL("../../../backend/src/CutWire.Studio.Infrastructure/Forge", import.meta.url)),
    emptyOutDir: false,
    minify: true,
    sourcemap: false,
    reportCompressedSize: false,
  },
  logLevel: "info",
})
