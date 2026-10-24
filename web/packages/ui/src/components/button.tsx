import { cva, type VariantProps } from "class-variance-authority"
import { Slot } from "radix-ui"
import type { ComponentProps } from "react"
import { cn } from "../cn"

export const buttonVariants = cva("min-h-9 text-sm", {
  variants: {
    variant: {
      primary: "btn-primary",
      secondary: "btn-secondary",
      ghost:
        "inline-flex cursor-pointer items-center justify-center gap-2 rounded-full font-semibold transition-colors hover:bg-muted disabled:pointer-events-none disabled:opacity-50",
      destructive:
        "inline-flex cursor-pointer items-center justify-center gap-2 rounded-full bg-destructive font-semibold text-destructive-foreground transition-opacity hover:opacity-90 disabled:pointer-events-none disabled:opacity-50",
    },
    size: {
      sm: "min-h-8 px-3",
      md: "px-4 py-2",
      lg: "min-h-11 px-6 py-2.5 text-base",
    },
  },
  defaultVariants: { variant: "primary", size: "md" },
})

export interface ButtonProps extends ComponentProps<"button">, VariantProps<typeof buttonVariants> {
  asChild?: boolean
}

export function Button({ className, variant, size, asChild, type = "button", ...props }: ButtonProps) {
  const Comp = asChild ? Slot.Root : "button"
  return <Comp type={asChild ? undefined : type} className={cn(buttonVariants({ variant, size }), className)} {...props} />
}

export function IconButton({
  label,
  className,
  variant = "ghost",
  ...props
}: Omit<ButtonProps, "size" | "aria-label"> & { label: string }) {
  return <Button aria-label={label} title={label} variant={variant} className={cn("size-10 p-0", className)} {...props} />
}
