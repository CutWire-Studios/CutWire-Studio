import { getApp, tiles } from "@cutwire/apps"
import { LayoutGrid } from "lucide-react"
import { Popover } from "radix-ui"
import type { ReactNode } from "react"
import { logo, logoWithName, logoWithNameLight } from "../assets"
import { cn } from "../cn"
import { useResolvedTheme } from "../theme"
import { Badge } from "./display"
import { AppIcon } from "./app-icon"
import { IconButton } from "./button"
import { ThemeToggle } from "./theme-toggle"

export function AppSwitcher() {
  return (
    <Popover.Root>
      <Popover.Trigger asChild>
        <IconButton label="CutWire apps" className="size-11">
          <LayoutGrid className="size-5" aria-hidden />
        </IconButton>
      </Popover.Trigger>
      <Popover.Portal>
        <Popover.Content
          align="end"
          sideOffset={12}
          collisionPadding={12}
          className="animate-pop z-50 w-[min(22rem,calc(100vw-1.5rem))] rounded-2xl border border-border bg-popover p-3 text-popover-foreground shadow-xl"
        >
          <ul className="grid grid-cols-3 gap-1">
            {tiles.map((t) => {
              const disabled = t.status === "coming-soon"
              return (
                <li key={t.id} className={`theme-${t.accent}`}>
                  <a
                    href={disabled ? undefined : t.href}
                    aria-disabled={disabled || undefined}
                    className={cn(
                      "flex flex-col items-center gap-1.5 rounded-xl p-2.5 text-center text-xs font-medium transition-colors",
                      disabled ? "cursor-not-allowed opacity-50" : "hover:bg-muted",
                    )}
                  >
                    <span className="flex size-10 items-center justify-center rounded-full bg-primary text-primary-foreground">
                      <AppIcon name={t.icon} className="size-5" />
                    </span>
                    {t.name}
                    {disabled && <Badge className="px-1.5 py-0 text-[10px]">Soon</Badge>}
                  </a>
                </li>
              )
            })}
          </ul>
        </Popover.Content>
      </Popover.Portal>
    </Popover.Root>
  )
}

export function AppHeader({ appId, account }: { appId?: string; account?: () => ReactNode }) {
  const dark = useResolvedTheme() === "dark"
  const app = appId ? getApp(appId) : undefined
  return (
    <header className="fixed inset-x-0 top-0 z-40">
      <div className="mx-auto flex max-w-6xl items-center justify-between gap-2 px-3 pt-3 md:gap-4 md:px-6 md:pt-4">
        <div className="glass flex min-h-11 min-w-0 items-center gap-1 rounded-full pr-4 pl-3">
          <a href="/" className="flex shrink-0 items-center transition-opacity hover:opacity-80" aria-label="CutWire Studio home">
            <img src={logo} alt="" className="h-7 w-auto sm:hidden" />
            <img src={dark ? logoWithName : logoWithNameLight} alt="CutWire" className="hidden h-8 w-auto sm:block" />
          </a>
          {app && (
            <a href={app.path} className={cn("theme-" + app.accent, "ml-2 flex min-w-0 items-center gap-2 border-l border-border pl-3")}>
              <AppIcon name={app.icon} className="size-4 shrink-0 text-primary" />
              <span className="text-label-md truncate">{app.name}</span>
            </a>
          )}
        </div>
        <div className="glass flex shrink-0 items-center gap-0.5 rounded-full px-1 py-0.5">
          <AppSwitcher />
          <ThemeToggle />
          {account && <div className="flex items-center pr-1 pl-0.5">{account()}</div>}
        </div>
      </div>
    </header>
  )
}
