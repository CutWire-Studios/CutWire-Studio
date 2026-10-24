import { Loader2 } from "lucide-react"
import { Avatar as RAvatar } from "radix-ui"
import type { ComponentProps, ReactNode } from "react"
import { Toaster as Sonner, toast } from "sonner"
import { cn } from "../cn"

export function Badge({ className, tone = "neutral", ...props }: ComponentProps<"span"> & { tone?: "neutral" | "primary" }) {
  return (
    <span
      className={cn(
        "text-eyebrow inline-flex items-center rounded-full px-2.5 py-0.5",
        tone === "primary" ? "bg-primary text-primary-foreground" : "bg-muted text-muted-foreground",
        className,
      )}
      {...props}
    />
  )
}

export function Card({ className, ...props }: ComponentProps<"div">) {
  return <div className={cn("panel p-6", className)} {...props} />
}

export function Avatar({ src, name, className }: { src?: string; name: string; className?: string }) {
  return (
    <RAvatar.Root className={cn("inline-flex size-9 shrink-0 items-center justify-center overflow-hidden rounded-full bg-muted", className)}>
      {src && <RAvatar.Image src={src} alt={name} className="size-full object-cover" />}
      <RAvatar.Fallback className="text-label-md uppercase">{name.slice(0, 2)}</RAvatar.Fallback>
    </RAvatar.Root>
  )
}

export function Skeleton({ className, ...props }: ComponentProps<"div">) {
  return <div aria-hidden className={cn("animate-pulse rounded-md bg-muted", className)} {...props} />
}

export function Spinner({ className }: { className?: string }) {
  return <Loader2 role="status" aria-label="Loading" className={cn("animate-spin-slow size-5", className)} />
}

export function EmptyState({
  icon,
  title,
  description,
  action,
  className,
}: {
  icon?: ReactNode
  title: string
  description?: string
  action?: ReactNode
  className?: string
}) {
  return (
    <div className={cn("flex flex-col items-center gap-3 px-6 py-16 text-center", className)}>
      {icon && <div className="flex size-14 items-center justify-center rounded-full bg-muted text-muted-foreground">{icon}</div>}
      <h2 className="text-headline-md">{title}</h2>
      {description && <p className="max-w-md text-muted-foreground">{description}</p>}
      {action && <div className="mt-2">{action}</div>}
    </div>
  )
}

export function Toaster(props: ComponentProps<typeof Sonner>) {
  return <Sonner position="bottom-right" {...props} />
}

export { toast }
