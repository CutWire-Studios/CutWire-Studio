import { AppHeader, Button, TooltipProvider, initTheme } from "@cutwire/ui"
import { Clapperboard, Download } from "lucide-react"
import { StrictMode } from "react"
import { createRoot } from "react-dom/client"
import "./index.css"

initTheme()

function App() {
  return (
    <div className="theme-drift">
      <AppHeader appId="edit" />
      <main className="container-cutwire flex min-h-dvh flex-col items-center justify-center gap-6 pt-24 pb-16 text-center">
        <span className="flex size-16 items-center justify-center rounded-full bg-primary text-primary-foreground">
          <Clapperboard className="size-8" aria-hidden />
        </span>
        <h1 className="text-display-xl">Drift Web is coming</h1>
        <p className="text-body-lg max-w-xl text-muted-foreground">
          We are bringing the Drift video editor to your browser. Until then, the desktop version is free and ready to use.
        </p>
        <Button asChild size="lg">
          <a href="https://cutwire.org/drift">
            <Download className="size-5" aria-hidden />
            Download Drift for desktop
          </a>
        </Button>
      </main>
    </div>
  )
}

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <TooltipProvider>
      <App />
    </TooltipProvider>
  </StrictMode>,
)
