import { NavLink, Outlet } from "react-router-dom"
import { cn } from "@cutwire/ui"
import { RequireAuth } from "../auth"

const tabs = [
  { to: "/account", label: "Profile", end: true },
  { to: "/account/security", label: "Security" },
  { to: "/account/tokens", label: "Tokens" },
  { to: "/account/apps", label: "Connected apps" },
  { to: "/account/danger", label: "Danger zone" },
]

export function SettingsLayout() {
  return (
    <RequireAuth>
      <div className="mx-auto max-w-3xl">
        <h1 className="text-headline-md">Account</h1>
        <nav aria-label="Account sections" className="mt-4 flex flex-wrap gap-1 rounded-2xl bg-muted p-1 sm:inline-flex sm:rounded-full">
          {tabs.map((t) => (
            <NavLink
              key={t.to}
              to={t.to}
              end={t.end}
              className={({ isActive }) =>
                cn(
                  "inline-flex min-h-8 items-center rounded-full px-4 text-sm font-medium text-muted-foreground transition-colors hover:text-foreground",
                  isActive && "bg-surface text-foreground shadow-sm",
                )
              }
            >
              {t.label}
            </NavLink>
          ))}
        </nav>
        <div className="mt-6 space-y-6">
          <Outlet />
        </div>
      </div>
    </RequireAuth>
  )
}
