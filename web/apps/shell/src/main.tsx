import { initTheme, TooltipProvider, Toaster } from "@cutwire/ui"
import { StrictMode } from "react"
import { createRoot } from "react-dom/client"
import { BrowserRouter } from "react-router-dom"
import { AuthProvider } from "./account/auth"
import { App } from "./app"
import "./index.css"

initTheme()

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <BrowserRouter>
      <TooltipProvider>
        <AuthProvider>
          <App />
        </AuthProvider>
        <Toaster />
      </TooltipProvider>
    </BrowserRouter>
  </StrictMode>,
)
