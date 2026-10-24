import {
  checkEnglish,
  checkProfanity,
  checkTaxonomy,
  checkText,
  checkTitle,
  type BloomFilter,
  type MetadataInput,
  type QualityRules,
  type Violation,
} from "@cutwire/content-rules"

/** Until the word list is loaded, "is this a dictionary word" answers yes so word-list gates stay silent. */
const PERMISSIVE = { mightContain: () => true } as unknown as BloomFilter

export function liveCheck(rules: QualityRules, bloom: BloomFilter | null, input: MetadataInput): Violation[] {
  return [
    ...checkTitle(rules, bloom ?? PERMISSIVE, input.title),
    ...checkText(rules, input.summary, input.description),
    ...(bloom ? checkEnglish(rules, bloom, input) : []),
    ...checkProfanity(rules, input),
    ...checkTaxonomy(rules, input),
  ]
}

export function byField(violations: Pick<Violation, "field">[] & Violation[]): Record<string, Violation[]> {
  const out: Record<string, Violation[]> = {}
  for (const v of violations) (out[v.field] ??= []).push(v)
  return out
}

/** Violations the server records as moderator flags instead of rejecting. */
export const FLAG_CODES = new Set(["similar_thumbnail"])
