import type { BloomFilter } from "./bloom";
import type { MetadataInput, QualityRules, Violation } from "./types";
import { violation, words } from "./text";

const SUFFIXES = ["s", "es", "ed", "ing", "ly", "er", "est"] as const;

/** Lookup candidates for a token, in the same order as `EnglishGate.Candidates` in the backend. */
export function candidates(w: string): string[] {
  const out = [w];
  if (w.length > 3 && w.endsWith("ies")) out.push(w.slice(0, -3) + "y");
  if (w.length > 3 && w.endsWith("ied")) out.push(w.slice(0, -3) + "y");
  for (const suffix of SUFFIXES) {
    if (w.length <= suffix.length + 2 || !w.endsWith(suffix)) continue;
    const stem = w.slice(0, -suffix.length);
    out.push(stem);
    if (suffix === "ed" || suffix === "ing" || suffix === "er" || suffix === "est") {
      out.push(stem + "e");
      if (stem.length > 2 && stem[stem.length - 1] === stem[stem.length - 2]) out.push(stem.slice(0, -1));
    }
  }
  return out;
}

export function isKnownWord(filter: BloomFilter, word: string): boolean {
  return candidates(word).some((c) => filter.mightContain(c));
}

export function checkEnglish(rules: QualityRules, filter: BloomFilter, input: MetadataInput): Violation[] {
  const ignored = new Set(rules.text.techTerms);
  for (const tag of input.tags) for (const w of words(tag)) ignored.add(w);
  const description = new Set(words(input.description));
  for (const w of words(input.title)) if (description.has(w)) ignored.add(w);

  let considered = 0;
  let known = 0;
  for (const token of words(`${input.summary}\n${input.description}`)) {
    if (/[0-9]/.test(token) || token.includes("'") || ignored.has(token)) continue;
    considered++;
    if (isKnownWord(filter, token)) known++;
  }

  if (considered < rules.englishMinTokens) return [];
  const ratio = known / considered;
  if (ratio < rules.englishMinRatio) {
    return [
      violation("description", "not_english", "Summary and description must be written in English.", {
        ratio: Math.round(ratio * 1000) / 1000,
        min: rules.englishMinRatio,
        tokens: considered,
      }),
    ];
  }
  return [];
}
