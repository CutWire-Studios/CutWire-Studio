import { cn } from "@cutwire/ui"
import { Outlet, NavLink } from "react-router-dom"
import { useMe } from "../account/auth"
import { isModerator } from "./roles"

const link = ({ isActive }: { isActive: boolean }) =>
  cn("text-label-md rounded-full px-4 py-2 transition-colors hover:bg-muted", isActive && "bg-muted text-primary")

export function CommunityLayout() {
  const { me } = useMe()
  return (
    <div className="space-y-8">
      <nav aria-label="Community" className="flex flex-wrap items-center gap-1">
        <NavLink to="/community" end className={link}>
          Home
        </NavLink>
        <NavLink to="/community/browse" className={link}>
          Browse
        </NavLink>
        <NavLink to="/community/upload" className={link}>
          Upload
        </NavLink>
        {me && (
          <NavLink to="/community/me" className={link}>
            My items
          </NavLink>
        )}
        {isModerator(me) && (
          <a href="/admin" className={link({ isActive: false })}>
            Admin
          </a>
        )}
      </nav>
      <Outlet />
    </div>
  )
}
