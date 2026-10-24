import { account, me as fetchMe, type Me } from "@cutwire/api-client"
import { createContext, use, useCallback, useEffect, useMemo, useState, type ReactNode } from "react"
import { Navigate, useLocation } from "react-router-dom"
import { Spinner } from "@cutwire/ui"
import { withReturn } from "./lib"

interface AuthState {
  /** undefined while loading, null when signed out */
  me: Me | null | undefined
  refresh: () => Promise<Me | null>
  signOut: () => Promise<void>
}

const AuthContext = createContext<AuthState | null>(null)

export function AuthProvider({ children }: { children: ReactNode }) {
  const [me, setMe] = useState<Me | null | undefined>(undefined)

  const refresh = useCallback(async () => {
    try {
      const next = await fetchMe()
      setMe(next)
      return next
    } catch {
      setMe((cur) => cur ?? null)
      return null
    }
  }, [])

  const signOut = useCallback(async () => {
    await account.signOut()
    setMe(null)
  }, [])

  useEffect(() => {
    void refresh()
  }, [refresh])

  const value = useMemo(() => ({ me, refresh, signOut }), [me, refresh, signOut])
  return <AuthContext value={value}>{children}</AuthContext>
}

export function useMe(): AuthState {
  const ctx = use(AuthContext)
  if (!ctx) throw new Error("useMe must be used inside AuthProvider")
  return ctx
}

export function RequireAuth({ children }: { children: ReactNode }) {
  const { me } = useMe()
  const loc = useLocation()
  if (me === undefined) return <Spinner className="mx-auto mt-24" />
  if (me === null) return <Navigate to={withReturn("/account/sign-in", loc.pathname + loc.search)} replace />
  return children
}
