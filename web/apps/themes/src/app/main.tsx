import { initTheme, TooltipProvider } from "@cutwire/ui"
import { StrictMode } from "react"
import { createRoot } from "react-dom/client"
import "@/styles/index.css"
import { App } from "./App"

initTheme()

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <TooltipProvider>
      <App />
    </TooltipProvider>
  </StrictMode>,
)
