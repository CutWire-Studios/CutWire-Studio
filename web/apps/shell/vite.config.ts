import tailwindcss from "@tailwindcss/vite"
import react from "@vitejs/plugin-react"
import { defineConfig } from "vite"

export default defineConfig({
  base: "/",
  plugins: [react(), tailwindcss()],
  build: { outDir: "../../dist/shell", emptyOutDir: true },
  // The browser sends Sec-Fetch-Site: same-origin to the dev server and the proxy forwards it untouched, which is how the API recognises the web app.
  server: { proxy: { "/api": process.env.CUTWIRE_API ?? "http://localhost:8087", "/files": process.env.CUTWIRE_API ?? "http://localhost:8087" } },
})
