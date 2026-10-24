import { me, type Me } from "@cutwire/api-client"
import { AppHeader, Avatar, Button, EmptyState } from "@cutwire/ui"
import { Hammer } from "lucide-react"
import { useEffect, useState } from "react"
import { Route, Routes, useLocation } from "react-router-dom"
import { Launcher } from "./launcher"

function AccountSlot() {
  const [user, setUser] = useState<Me | null | undefined>(undefined)
  useEffect(() => {
    me()
      .then(setUser)
      .catch(() => setUser(null))
  }, [])
  if (user === undefined) return null
  if (user === null)
    return (
      <Button asChild size="sm" className="ml-1">
        <a href="/account/sign-in">Sign in</a>
      </Button>
    )
  return (
    <a href="/account" aria-label="Account">
      <Avatar name={user.displayName ?? user.userName} src={user.avatarUrl} />
    </a>
  )
}

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
      <AppHeader appId={section === "community" || section === "stock" ? section : undefined} account={() => <AccountSlot />} />
      <main className="container-cutwire pt-24 pb-section">
        <Routes>
          <Route path="/" element={<Launcher />} />
          <Route path="/community/*" element={<ComingSoon title="Community" />} />
          <Route path="/stock/*" element={<ComingSoon title="Stock Library" />} />
          <Route path="/account/*" element={<ComingSoon title="Account" />} />
          <Route path="*" element={<ComingSoon title="Page not found" />} />
        </Routes>
      </main>
    </>
  )
}
