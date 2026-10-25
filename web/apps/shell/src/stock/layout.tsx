import { cn } from "@cutwire/ui"
import { NavLink, Outlet } from "react-router-dom"

const link = ({ isActive }: { isActive: boolean }) =>
  cn("text-label-md rounded-full px-4 py-2 transition-colors hover:bg-muted", isActive && "bg-muted text-primary")

export function StockLayout() {
  return (
    <div className="space-y-8">
      <nav aria-label="Stock Library" className="flex flex-wrap items-center gap-1">
        <NavLink to="/stock" end className={link}>
          Home
        </NavLink>
        <NavLink to="/stock/search" className={link}>
          Search
        </NavLink>
        <NavLink to="/stock/sfx" className={link}>
          Sound effects
        </NavLink>
        <NavLink to="/stock/drift-assets" className={link}>
          Drift assets
        </NavLink>
      </nav>
      <Outlet />
    </div>
  )
}

export function DriftNote() {
  return <p className="text-sm text-muted-foreground">Drift users: this library is also available inside the Drift editor.</p>
}
