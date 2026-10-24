import { initTheme, TooltipProvider, Toaster } from "@cutwire/ui"
import { StrictMode } from "react"
import { createRoot } from "react-dom/client"
import { BrowserRouter } from "react-router-dom"
import { App } from "./app"
import "./index.css"

initTheme()

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <BrowserRouter>
      <TooltipProvider>
        <App />
        <Toaster />
      </TooltipProvider>
    </BrowserRouter>
  </StrictMode>,
)
