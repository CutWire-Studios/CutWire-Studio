import type { FieldViolation, ItemDetail, KindInfo } from "@cutwire/api-client"
import { Button, DropZone, cn } from "@cutwire/ui"
import { FileBox } from "lucide-react"
import { formatBytes } from "../lib/text"
import { FieldErrors } from "./shared"

export function checkPackageFile(file: { name: string; size: number }, kind: KindInfo): string | null {
  const ext = file.name.includes(".") ? file.name.slice(file.name.lastIndexOf(".")).toLowerCase() : ""
  const accepted = kind.acceptedExtensions.map((e) => (e.startsWith(".") ? e : `.${e}`).toLowerCase())
  if (accepted.length && !accepted.includes(ext)) return `${kind.displayName} packages must be ${accepted.join(" or ")}.`
  if (file.size === 0) return "This file is empty."
  const cap = Number(kind.maxPackageBytes)
  if (file.size > cap) return `Package is ${formatBytes(file.size)}; the limit is ${formatBytes(cap)}.`
  return null
}

export function PackageStep({
  kinds,
  kind,
  onKind,
  file,
  onFile,
  item,
  problems,
  clientError,
  setClientError,
  lockKind,
}: {
  kinds: KindInfo[]
  kind: string
  onKind: (k: string) => void
  file: File | null
  onFile: (f: File | null) => void
  item: ItemDetail | null
  problems: FieldViolation[]
  clientError: string | null
  setClientError: (e: string | null) => void
  lockKind: boolean
}) {
  const info = kinds.find((k) => k.id === kind)
  const accept = info?.acceptedExtensions.map((e) => (e.startsWith(".") ? e : `.${e}`)).join(",")
  const current = item?.versions.find((v) => v.isCurrent) ?? item?.versions[0]

  const pick = (files: File[]) => {
    const f = files[0]
    if (!f || !info) return
    const err = checkPackageFile(f, info)
    setClientError(err)
    onFile(err ? null : f)
  }

  return (
    <div className="space-y-6">
      <fieldset className="space-y-2" disabled={lockKind}>
        <legend className="text-label-md">What are you sharing?</legend>
        <div className="flex flex-wrap gap-2">
          {kinds.map((k) => (
            <button
              key={k.id}
              type="button"
              aria-pressed={k.id === kind}
              onClick={() => {
                onKind(k.id)
                onFile(null)
                setClientError(null)
              }}
              className={cn(
                "text-label-md cursor-pointer rounded-full border px-4 py-1.5 transition-colors disabled:cursor-not-allowed",
                k.id === kind ? "border-primary bg-primary text-primary-foreground" : "border-border hover:bg-muted",
              )}
            >
              {k.displayName}
            </button>
          ))}
        </div>
      </fieldset>

      {info && (
        <div className="space-y-3">
          <DropZone label="Choose package file" accept={accept} onFiles={pick} className="min-h-40">
            <FileBox className="size-8 text-muted-foreground" aria-hidden />
            {file ? (
              <>
                <p className="font-semibold">{file.name}</p>
                <p className="text-sm text-muted-foreground">{formatBytes(file.size)}. Drop another file to replace it.</p>
              </>
            ) : (
              <>
                <p className="font-semibold">Drop your {info.acceptedExtensions.join(" / ")} file here, or click to browse</p>
                <p className="text-sm text-muted-foreground">Up to {formatBytes(Number(info.maxPackageBytes))}.</p>
              </>
            )}
          </DropZone>
          {clientError && <p role="alert" className="text-sm text-destructive">{clientError}</p>}
          <FieldErrors violations={problems} />
          {!file && current && (
            <p className="text-sm text-muted-foreground">
              Package already uploaded (version {current.version}, {formatBytes(Number(current.size))}). Choose a file only if you want to replace it.
            </p>
          )}
          {file && (
            <Button variant="ghost" size="sm" onClick={() => onFile(null)}>
              Remove file
            </Button>
          )}
        </div>
      )}
    </div>
  )
}
