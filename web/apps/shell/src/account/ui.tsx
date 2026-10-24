import { Button, Input, Spinner, cn } from "@cutwire/ui"
import { AlertCircle } from "lucide-react"
import { useCallback, useId, useState, type ComponentProps, type ReactNode } from "react"
import { errorMessage, passwordStrength } from "./lib"

export function AuthCard({ title, description, children, footer, wide }: { title: string; description?: ReactNode; children: ReactNode; footer?: ReactNode; wide?: boolean }) {
  return (
    <section className={cn("panel animate-rise mx-auto mt-4 w-full p-6 sm:p-8", wide ? "max-w-xl" : "max-w-md")}>
      <h1 className="text-headline-md">{title}</h1>
      {description && <p className="mt-2 text-muted-foreground">{description}</p>}
      <div className="mt-6">{children}</div>
      {footer && <div className="mt-6 border-t border-border pt-4 text-center text-sm text-muted-foreground">{footer}</div>}
    </section>
  )
}

export function FormError({ message }: { message?: string | null }) {
  if (!message) return null
  return (
    <div role="alert" className="flex items-start gap-2 rounded-lg border border-destructive/40 bg-destructive/10 px-3 py-2 text-sm text-destructive">
      <AlertCircle className="mt-0.5 size-4 shrink-0" aria-hidden />
      <span>{message}</span>
    </div>
  )
}

export function Notice({ children }: { children: ReactNode }) {
  return (
    <div role="status" className="rounded-lg border border-border bg-muted px-3 py-2 text-sm">
      {children}
    </div>
  )
}

export function TextField({
  label,
  error,
  hint,
  className,
  children,
  ...props
}: ComponentProps<typeof Input> & { label: string; error?: string | null; hint?: ReactNode; children?: ReactNode }) {
  const id = useId()
  const describedBy = [error ? `${id}-err` : null, hint ? `${id}-hint` : null].filter(Boolean).join(" ") || undefined
  return (
    <div className={cn("space-y-1.5", className)}>
      <label htmlFor={id} className="text-label-md block">
        {label}
      </label>
      <Input id={id} aria-invalid={error ? true : undefined} aria-describedby={describedBy} {...props} />
      {children}
      {hint && !error && (
        <p id={`${id}-hint`} className="text-xs text-muted-foreground">
          {hint}
        </p>
      )}
      {error && (
        <p id={`${id}-err`} className="text-xs text-destructive">
          {error}
        </p>
      )}
    </div>
  )
}

export function SubmitButton({ pending, children, ...props }: ComponentProps<typeof Button> & { pending?: boolean }) {
  return (
    <Button type="submit" disabled={pending || props.disabled} {...props}>
      {pending && <Spinner className="size-4" />}
      {children}
    </Button>
  )
}

export function PasswordMeter({ password }: { password: string }) {
  const { score, label } = passwordStrength(password)
  if (!password) return null
  const tone = ["bg-destructive", "bg-destructive", "bg-amber-500", "bg-lime-500", "bg-green-600"][score]
  return (
    <div aria-live="polite">
      <div className="flex gap-1" aria-hidden>
        {[0, 1, 2, 3].map((i) => (
          <span key={i} className={cn("h-1.5 flex-1 rounded-full bg-muted", i < score && tone)} />
        ))}
      </div>
      <p className="mt-1 text-xs text-muted-foreground">Password strength: {label}</p>
    </div>
  )
}

/** Tracks the pending flag and last error of an async action. */
export function useAction() {
  const [pending, setPending] = useState(false)
  const [error, setError] = useState<unknown>(null)
  const run = useCallback(async <T,>(fn: () => Promise<T>): Promise<T | undefined> => {
    setPending(true)
    setError(null)
    try {
      return await fn()
    } catch (e) {
      setError(e)
      return undefined
    } finally {
      setPending(false)
    }
  }, [])
  return { pending, error, run, message: error ? errorMessage(error) : null, reset: () => setError(null) }
}

export function Section({ title, description, children }: { title: string; description?: ReactNode; children: ReactNode }) {
  return (
    <section className="panel p-6">
      <h2 className="text-lg font-semibold">{title}</h2>
      {description && <p className="mt-1 text-sm text-muted-foreground">{description}</p>}
      <div className="mt-4">{children}</div>
    </section>
  )
}
