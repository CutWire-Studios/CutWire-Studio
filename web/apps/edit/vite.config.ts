import tailwindcss from "@tailwindcss/vite"
import react from "@vitejs/plugin-react"
import { defineConfig } from "vite"

export default defineConfig({
  base: "/edit/",
  plugins: [react(), tailwindcss()],
  build: { outDir: "../../dist/edit", emptyOutDir: true },
})
