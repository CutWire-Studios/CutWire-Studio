import { useId, useState, type DragEvent, type ReactNode } from "react"
import { cn } from "../cn"

/** File drop target that also works as a click/keyboard file picker. */
export function DropZone({
  onFiles,
  accept,
  multiple,
  disabled,
  className,
  children,
  label,
}: {
  onFiles: (files: File[]) => void
  accept?: string
  multiple?: boolean
  disabled?: boolean
  className?: string
  children: ReactNode
  label: string
}) {
  const id = useId()
  const [over, setOver] = useState(false)
  const handle = (files: FileList | null) => {
    if (files && files.length > 0) onFiles(Array.from(files))
  }
  const onDrop = (e: DragEvent) => {
    e.preventDefault()
    setOver(false)
    if (!disabled) handle(e.dataTransfer.files)
  }
  return (
    <label
      htmlFor={id}
      onDragOver={(e) => {
        e.preventDefault()
        if (!disabled) setOver(true)
      }}
      onDragLeave={() => setOver(false)}
      onDrop={onDrop}
      className={cn(
        "flex cursor-pointer flex-col items-center justify-center gap-2 rounded-xl border-2 border-dashed border-border p-6 text-center transition-colors focus-within:ring-2 focus-within:ring-ring hover:bg-muted/50",
        over && "border-primary bg-muted",
        disabled && "pointer-events-none opacity-50",
        className,
      )}
    >
      <input
        id={id}
        type="file"
        aria-label={label}
        className="sr-only"
        accept={accept}
        multiple={multiple}
        disabled={disabled}
        onChange={(e) => {
          handle(e.currentTarget.files)
          e.currentTarget.value = ""
        }}
      />
      {children}
    </label>
  )
}
