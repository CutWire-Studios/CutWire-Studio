import { X } from "lucide-react"
import { Dialog as RDialog, DropdownMenu as RMenu, Tabs as RTabs, Tooltip as RTooltip } from "radix-ui"
import type { ComponentProps, ReactNode } from "react"
import { cn } from "../cn"

export const Dialog = RDialog.Root
export const DialogTrigger = RDialog.Trigger
export const DialogClose = RDialog.Close

export function DialogContent({
  title,
  description,
  className,
  children,
  ...props
}: ComponentProps<typeof RDialog.Content> & { title: string; description?: string }) {
  return (
    <RDialog.Portal>
      <RDialog.Overlay className="animate-fade fixed inset-0 z-50 bg-black/50 backdrop-blur-sm" />
      <RDialog.Content
        className={cn(
          "animate-pop fixed top-1/2 left-1/2 z-50 w-[calc(100vw-2rem)] max-w-lg -translate-x-1/2 -translate-y-1/2 rounded-xl border border-border bg-popover p-6 text-popover-foreground shadow-xl",
          className,
        )}
        {...props}
      >
        <RDialog.Title className="text-lg font-semibold">{title}</RDialog.Title>
        <RDialog.Description className={cn("mt-1 text-sm text-muted-foreground", !description && "sr-only")}>
          {description ?? title}
        </RDialog.Description>
        <div className="mt-4">{children}</div>
        <RDialog.Close
          aria-label="Close"
          className="absolute top-3 right-3 inline-flex size-9 cursor-pointer items-center justify-center rounded-full hover:bg-muted"
        >
          <X className="size-4" aria-hidden />
        </RDialog.Close>
      </RDialog.Content>
    </RDialog.Portal>
  )
}

export const DropdownMenu = RMenu.Root
export const DropdownMenuTrigger = RMenu.Trigger

export function DropdownMenuContent({ className, sideOffset = 8, ...props }: ComponentProps<typeof RMenu.Content>) {
  return (
    <RMenu.Portal>
      <RMenu.Content
        sideOffset={sideOffset}
        className={cn(
          "animate-pop z-50 min-w-44 rounded-xl border border-border bg-popover p-1 text-popover-foreground shadow-lg",
          className,
        )}
        {...props}
      />
    </RMenu.Portal>
  )
}

export function DropdownMenuItem({ className, ...props }: ComponentProps<typeof RMenu.Item>) {
  return (
    <RMenu.Item
      className={cn(
        "flex min-h-9 cursor-pointer items-center gap-2 rounded-md px-3 text-sm outline-none data-[disabled]:pointer-events-none data-[disabled]:opacity-50 data-[highlighted]:bg-muted",
        className,
      )}
      {...props}
    />
  )
}

export const DropdownMenuSeparator = (p: ComponentProps<typeof RMenu.Separator>) => (
  <RMenu.Separator className={cn("my-1 h-px bg-border", p.className)} {...p} />
)

export const TooltipProvider = RTooltip.Provider

export function Tooltip({ content, children }: { content: ReactNode; children: ReactNode }) {
  return (
    <RTooltip.Root>
      <RTooltip.Trigger asChild>{children}</RTooltip.Trigger>
      <RTooltip.Portal>
        <RTooltip.Content
          sideOffset={6}
          className="animate-pop z-50 rounded-md bg-foreground px-2.5 py-1.5 text-xs font-medium text-background"
        >
          {content}
        </RTooltip.Content>
      </RTooltip.Portal>
    </RTooltip.Root>
  )
}

export const Tabs = RTabs.Root

export function TabsList({ className, ...props }: ComponentProps<typeof RTabs.List>) {
  return <RTabs.List className={cn("inline-flex items-center gap-1 rounded-full bg-muted p-1", className)} {...props} />
}

export function TabsTrigger({ className, ...props }: ComponentProps<typeof RTabs.Trigger>) {
  return (
    <RTabs.Trigger
      className={cn(
        "min-h-8 cursor-pointer rounded-full px-4 text-sm font-medium text-muted-foreground transition-colors data-[state=active]:bg-surface data-[state=active]:text-foreground data-[state=active]:shadow-sm",
        className,
      )}
      {...props}
    />
  )
}

export function TabsContent({ className, ...props }: ComponentProps<typeof RTabs.Content>) {
  return <RTabs.Content className={cn("mt-4", className)} {...props} />
}
