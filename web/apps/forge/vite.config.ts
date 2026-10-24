/// <reference types="vitest/config" />
import tailwindcss from "@tailwindcss/vite"
import react from "@vitejs/plugin-react"
import { fileURLToPath, URL } from "node:url"
import { defineConfig } from "vite"

export default defineConfig({
  base: "/forge/",
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: { "@": fileURLToPath(new URL("./src", import.meta.url)) },
  },
  optimizeDeps: {
    // The wasm is located relative to the module's own URL; pre-bundling moves the module and
    // breaks that lookup.
    exclude: ["@bokuweb/zstd-wasm"],
  },
  server: {
    proxy: {
      "/api": "http://localhost:8087",
      "/account": "http://localhost:8087",
      "/mcp": "http://localhost:8087",
    },
  },
  build: {
    target: "es2022",
    outDir: "../../dist/forge",
    emptyOutDir: true,
  },
  worker: {
    // The default IIFE format rewrites import.meta.url (used by the Emscripten glue) to
    // self.location.href, and AudioWorkletGlobalScope has no `self`.
    format: "es",
  },
  test: {
    environment: "node",
    include: ["src/**/*.test.ts"],
  },
})
