import "@cutwire/ui/ui.css"
import "@xyflow/react/dist/base.css"
import "@/styles/tokens.css"
import "@/styles/base.css"
import "@/styles/app.css"
import { initTheme, TooltipProvider } from "@cutwire/ui"
import { StrictMode } from "react"
import { createRoot } from "react-dom/client"
import { App } from "./App"
import { startCloudSync } from "./cloudSync"

initTheme()
startCloudSync()

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <TooltipProvider>
      <App />
    </TooltipProvider>
  </StrictMode>,
)
