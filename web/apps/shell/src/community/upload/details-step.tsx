import type { FieldViolation, KindInfo } from "@cutwire/api-client"
import type { QualityRules, Violation } from "@cutwire/content-rules"
import { Input, Select, Textarea, cn } from "@cutwire/ui"
import { useId, useMemo, useState, type ReactNode } from "react"
import { byField } from "../lib/live-check"
import { FieldErrors } from "./shared"

export interface Details {
  title: string
  summary: string
  description: string
  category: string
  tags: string[]
  license: string
}

export const EMPTY_DETAILS: Details = { title: "", summary: "", description: "", category: "", tags: [], license: "" }

function Field({ label, counter, children, errors, hint, htmlFor }: { label: string; counter?: { text: string; ok: boolean }; children: ReactNode; errors?: Violation[]; hint?: string; htmlFor?: string }) {
  return (
    <div className="space-y-1.5">
      <div className="flex items-baseline justify-between gap-2">
        <label htmlFor={htmlFor} className="text-label-md">
          {label}
        </label>
        {counter && <span className={cn("text-xs tabular-nums", counter.ok ? "text-muted-foreground" : "text-destructive")}>{counter.text}</span>}
      </div>
      {children}
      {hint && !errors?.length && <p className="text-xs text-muted-foreground">{hint}</p>}
      <FieldErrors violations={errors} />
    </div>
  )
}

const counter = (len: number, r: { min: number; max: number }) => ({ text: `${len} / ${r.max}`, ok: len >= r.min && len <= r.max })

export function DetailsStep({
  kind,
  rules,
  value,
  onChange,
  live,
  server,
  bloomState,
  onWantBloom,
}: {
  kind: KindInfo
  rules: QualityRules
  value: Details
  onChange: (d: Details) => void
  live: Violation[]
  server: FieldViolation[]
  bloomState: "idle" | "loading" | "ready" | "error"
  onWantBloom: () => void
}) {
  const ids = { title: useId(), summary: useId(), description: useId() }
  const [touched, setTouched] = useState<Set<string>>(new Set())
  const touch = (f: string) => setTouched((t) => new Set(t).add(f))
  const set = (patch: Partial<Details>) => onChange({ ...value, ...patch })

  const errors = useMemo(() => {
    const all = byField([...live, ...(server as Violation[]).filter((s) => !live.some((l) => l.field === s.field && l.code === s.code))])
    const shown: Record<string, Violation[]> = {}
    for (const [f, vs] of Object.entries(all)) {
      const serverHas = server.some((s) => s.field === f)
      if (touched.has(f) || serverHas || (f === "tags" && touched.has("tags"))) shown[f] = vs
    }
    return shown
  }, [live, server, touched])

  const toggleTag = (t: string) => {
    touch("tags")
    set({ tags: value.tags.includes(t) ? value.tags.filter((x) => x !== t) : [...value.tags, t] })
  }
  const tagCount = { text: `${value.tags.length} / ${rules.maxTags}`, ok: value.tags.length >= rules.minTags && value.tags.length <= rules.maxTags }

  return (
    <div className="space-y-6">
      <Field label="Title" htmlFor={ids.title} counter={counter(value.title.length, rules.title)} errors={errors.title} hint="Plain English, no URLs or all caps.">
        <Input id={ids.title} value={value.title} onChange={(e) => set({ title: e.target.value })} onBlur={() => touch("title")} aria-invalid={!!errors.title} />
      </Field>
      <Field label="Summary" htmlFor={ids.summary} counter={counter(value.summary.length, rules.summary)} errors={errors.summary} hint="One sentence, no links.">
        <Input id={ids.summary} value={value.summary} onChange={(e) => set({ summary: e.target.value })} onBlur={() => touch("summary")} aria-invalid={!!errors.summary} />
      </Field>
      <Field
        label="Description"
        htmlFor={ids.description}
        counter={counter(value.description.length, rules.description)}
        errors={errors.description}
        hint={bloomState === "loading" ? "Loading the English word list..." : bloomState === "error" ? "Could not load the word list; the server will still check your text." : `Written in English. Up to ${rules.descriptionMaxLinks} links.`}
      >
        <Textarea
          id={ids.description}
          rows={8}
          value={value.description}
          onFocus={onWantBloom}
          onChange={(e) => set({ description: e.target.value })}
          onBlur={() => touch("description")}
          aria-invalid={!!errors.description}
        />
      </Field>

      <div className="grid gap-6 sm:grid-cols-2">
        <Field label="Category" errors={errors.category}>
          <Select
            value={value.category}
            onValueChange={(v) => {
              touch("category")
              set({ category: v })
            }}
            placeholder="Choose a category"
            options={kind.categories.map((c) => ({ value: c, label: c }))}
          />
        </Field>
        <Field label="License" errors={errors.license}>
          <Select
            value={value.license}
            onValueChange={(v) => {
              touch("license")
              set({ license: v })
            }}
            placeholder="Choose a license"
            options={rules.licenses.map((l) => ({ value: l, label: l }))}
          />
        </Field>
      </div>

      <Field label="Tags" counter={tagCount} errors={errors.tags} hint={`Pick ${rules.minTags} to ${rules.maxTags}.`}>
        <div className="flex flex-wrap gap-1.5" role="group" aria-label="Tags">
          {kind.tags.map((t) => {
            const on = value.tags.includes(t)
            return (
              <button
                key={t}
                type="button"
                aria-pressed={on}
                onClick={() => toggleTag(t)}
                className={cn("text-label-md cursor-pointer rounded-full border px-3 py-1 transition-colors", on ? "border-primary bg-primary text-primary-foreground" : "border-border hover:bg-muted")}
              >
                {t}
              </button>
            )
          })}
        </div>
      </Field>
    </div>
  )
}
