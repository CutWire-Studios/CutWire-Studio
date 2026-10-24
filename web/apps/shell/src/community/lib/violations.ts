import { ApiError, type FieldViolation } from "@cutwire/api-client"

export type Step = "package" | "details" | "media" | "review"
export const STEPS: { id: Step; label: string }[] = [
  { id: "package", label: "Package" },
  { id: "details", label: "Details" },
  { id: "media", label: "Media" },
  { id: "review", label: "Review" },
]

const DETAIL_FIELDS = new Set(["title", "summary", "description", "category", "tags", "license"])

/** Maps a violation's field to the wizard step that can fix it. Account/quota problems have no step to fix them in. */
export function stepForViolation(v: Pick<FieldViolation, "field">): Step {
  const f = v.field
  if (f === "package" || f === "kind") return "package"
  if (DETAIL_FIELDS.has(f)) return "details"
  if (f === "media" || f.startsWith("media.") || f.startsWith("media[")) return "media"
  return "review"
}

export function groupByStep(violations: FieldViolation[]): Record<Step, FieldViolation[]> {
  const out: Record<Step, FieldViolation[]> = { package: [], details: [], media: [], review: [] }
  for (const v of violations) out[stepForViolation(v)].push(v)
  return out
}

/** The first step (in wizard order) that has a violation, or null. */
export function firstStepWithViolations(violations: FieldViolation[]): Step | null {
  const g = groupByStep(violations)
  return STEPS.find((s) => g[s.id].length > 0)?.id ?? null
}

/** Violations carried by an error: the stable `violations` list, or one synthesized per field error as a fallback. */
export function violationsOf(err: unknown): FieldViolation[] {
  if (!(err instanceof ApiError)) return []
  if (err.violations?.length) return err.violations
  if (err.errors) {
    return Object.entries(err.errors).flatMap(([field, msgs]) => msgs.map((message) => ({ field, code: err.code, message })))
  }
  if (err.status === 400 || err.status === 413) return [{ field: "package", code: err.code, message: err.detail ?? err.title }]
  return []
}

const pct = (r: unknown) => `${Math.round(Number(r) * 100)}%`

/** Code-specific wording. Falls back to the server / content-rules message. */
export function violationMessage(v: Pick<FieldViolation, "code" | "message" | "params">): string {
  const p = (v.params ?? {}) as Record<string, unknown>
  switch (v.code) {
    case "not_english":
      return `English words: ${pct(p.ratio)} / ${pct(p.min)} required.`
    case "too_many_links":
      return `Links: ${p.count} / ${p.max} allowed.`
    case "non_ascii":
      return `${v.message} Found "${String(p.char ?? "")}" at position ${Number(p.index ?? 0) + 1}.`
    case "duplicate_package":
      return "This exact package has already been uploaded."
    case "similar_thumbnail":
      return "The thumbnail looks very similar to an existing item. Moderators will take a look."
    case "upload_limit":
      return "You have reached today's upload limit for your trust level."
    default:
      return v.message
  }
}

/** "62 / 140" style counters for length-bound fields. */
export function lengthCounter(len: number, rule: { min: number; max: number }): { text: string; ok: boolean } {
  return { text: `${len} / ${rule.max}`, ok: len >= rule.min && len <= rule.max }
}
