import { Check, ChevronDown } from "lucide-react"
import { Checkbox as RCheckbox, Select as RSelect, Switch as RSwitch } from "radix-ui"
import type { ComponentProps, ReactNode } from "react"
import { cn } from "../cn"

const field =
  "w-full rounded-lg border border-border bg-input px-3 py-2 text-sm text-foreground placeholder:text-muted-foreground transition-colors focus-visible:border-ring focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring/40 disabled:cursor-not-allowed disabled:opacity-50 aria-invalid:border-destructive"

export function Input({ className, ...props }: ComponentProps<"input">) {
  return <input className={cn(field, "min-h-10", className)} {...props} />
}

export function Textarea({ className, ...props }: ComponentProps<"textarea">) {
  return <textarea className={cn(field, "min-h-24 resize-y", className)} {...props} />
}

export function Checkbox({ className, ...props }: ComponentProps<typeof RCheckbox.Root>) {
  return (
    <RCheckbox.Root
      className={cn(
        "flex size-5 shrink-0 cursor-pointer items-center justify-center rounded-sm border border-border bg-input transition-colors data-[state=checked]:border-primary data-[state=checked]:bg-primary data-[state=checked]:text-primary-foreground disabled:cursor-not-allowed disabled:opacity-50",
        className,
      )}
      {...props}
    >
      <RCheckbox.Indicator>
        <Check className="size-3.5" aria-hidden />
      </RCheckbox.Indicator>
    </RCheckbox.Root>
  )
}

export function Switch({ className, ...props }: ComponentProps<typeof RSwitch.Root>) {
  return (
    <RSwitch.Root
      className={cn(
        "inline-flex h-6 w-11 shrink-0 cursor-pointer items-center rounded-full border border-border bg-muted p-0.5 transition-colors data-[state=checked]:border-primary data-[state=checked]:bg-primary disabled:cursor-not-allowed disabled:opacity-50",
        className,
      )}
      {...props}
    >
      <RSwitch.Thumb className="block size-4.5 rounded-full bg-foreground transition-transform data-[state=checked]:translate-x-5 data-[state=checked]:bg-primary-foreground" />
    </RSwitch.Root>
  )
}

export interface SelectOption {
  value: string
  label: ReactNode
}

export function Select({
  options,
  placeholder,
  className,
  ...props
}: ComponentProps<typeof RSelect.Root> & { options: SelectOption[]; placeholder?: string; className?: string }) {
  return (
    <RSelect.Root {...props}>
      <RSelect.Trigger className={cn(field, "inline-flex min-h-10 cursor-pointer items-center justify-between gap-2", className)}>
        <RSelect.Value placeholder={placeholder} />
        <RSelect.Icon>
          <ChevronDown className="size-4 text-muted-foreground" aria-hidden />
        </RSelect.Icon>
      </RSelect.Trigger>
      <RSelect.Portal>
        <RSelect.Content
          position="popper"
          sideOffset={6}
          className="animate-pop z-50 min-w-(--radix-select-trigger-width) overflow-hidden rounded-lg border border-border bg-popover p-1 text-popover-foreground shadow-lg"
        >
          <RSelect.Viewport>
            {options.map((o) => (
              <RSelect.Item
                key={o.value}
                value={o.value}
                className="flex min-h-9 cursor-pointer items-center justify-between gap-2 rounded-md px-3 text-sm outline-none data-[highlighted]:bg-muted"
              >
                <RSelect.ItemText>{o.label}</RSelect.ItemText>
                <RSelect.ItemIndicator>
                  <Check className="size-4" aria-hidden />
                </RSelect.ItemIndicator>
              </RSelect.Item>
            ))}
          </RSelect.Viewport>
        </RSelect.Content>
      </RSelect.Portal>
    </RSelect.Root>
  )
}
