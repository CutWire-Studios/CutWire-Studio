import { AppHeader, Button, EmptyState } from "@cutwire/ui"
import { Hammer } from "lucide-react"
import { Route, Routes, useLocation } from "react-router-dom"
import { AccountMenu } from "./account/account-menu"
import { accountRoutes } from "./account/routes"
import { communityRoutes } from "./community/routes"
import { Launcher } from "./launcher"

function ComingSoon({ title }: { title: string }) {
  return (
    <EmptyState
      className="min-h-[60dvh] justify-center"
      icon={<Hammer className="size-6" aria-hidden />}
      title={title}
      description="This part of CutWire Studio is coming soon in this build."
      action={
        <Button asChild variant="secondary">
          <a href="/">Back to launcher</a>
        </Button>
      }
    />
  )
}

export function App() {
  const { pathname } = useLocation()
  const section = pathname.split("/")[1]
  return (
    <>
      <AppHeader appId={section === "community" || section === "stock" ? section : undefined} account={() => <AccountMenu />} />
      <main className="container-cutwire pt-24 pb-section">
        <Routes>
          <Route path="/" element={<Launcher />} />
          {communityRoutes}
          <Route path="/stock/*" element={<ComingSoon title="Stock Library" />} />
          {accountRoutes}
          <Route path="*" element={<ComingSoon title="Page not found" />} />
        </Routes>
      </main>
    </>
  )
}
