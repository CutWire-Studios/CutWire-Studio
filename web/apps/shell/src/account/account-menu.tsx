import { Avatar, Button, DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuSeparator, DropdownMenuTrigger } from "@cutwire/ui"
import { Link, useLocation } from "react-router-dom"
import { useMe } from "./auth"
import { withReturn } from "./lib"

const items = [
  { to: "/account", label: "Profile" },
  { to: "/account/security", label: "Security" },
  { to: "/account/tokens", label: "Tokens" },
  { to: "/account/apps", label: "Connected apps" },
]

export function AccountMenu() {
  const { me, signOut } = useMe()
  const { pathname, search } = useLocation()
  if (me === undefined) return null
  if (me === null)
    return (
      <Button asChild size="sm" className="ml-1">
        <Link to={pathname.startsWith("/account") ? "/account/sign-in" : withReturn("/account/sign-in", pathname + search)}>Sign in</Link>
      </Button>
    )
  const name = me.displayName || me.handle
  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <button type="button" aria-label="Account menu" className="cursor-pointer rounded-full">
          <Avatar name={name} src={me.avatarUrl ?? undefined} />
        </button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end">
        <div className="px-3 py-2">
          <p className="truncate text-sm font-semibold">{name}</p>
          <p className="truncate text-xs text-muted-foreground">@{me.handle}</p>
        </div>
        <DropdownMenuSeparator />
        {items.map((i) => (
          <DropdownMenuItem key={i.to} asChild>
            <Link to={i.to}>{i.label}</Link>
          </DropdownMenuItem>
        ))}
        <DropdownMenuSeparator />
        <DropdownMenuItem onSelect={() => void signOut()}>Sign out</DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  )
}
