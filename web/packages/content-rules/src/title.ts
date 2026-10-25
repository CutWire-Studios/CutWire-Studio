import type { BloomFilter } from "./bloom";
import { isKnownWord } from "./english";
import type { QualityRules, Violation } from "./types";
import { codePoint, firstNonAscii, hasLink, violation } from "./text";

const FIELD = "title";

export function checkTitle(rules: QualityRules, filter: BloomFilter, title: string): Violation[] {
  const found: Violation[] = [];
  if (title.length === 0) return [violation(FIELD, "required", "Title is required.")];

  const bad = firstNonAscii(title, false);
  if (bad >= 0) {
    found.push(violation(FIELD, "non_ascii", "Title may only use printable ASCII characters.", { index: bad, char: codePoint(title, bad) }));
  }
  if (title.length < rules.title.min) {
    found.push(violation(FIELD, "too_short", `Title must be at least ${rules.title.min} characters.`, { min: rules.title.min }));
  }
  if (title.length > rules.title.max) {
    found.push(violation(FIELD, "too_long", `Title must be at most ${rules.title.max} characters.`, { max: rules.title.max }));
  }
  if (title !== title.trim() || title.includes("  ")) {
    found.push(violation(FIELD, "bad_whitespace", "Title must not have leading, trailing or doubled spaces."));
  }

  const parts = title.split(" ").filter((p) => p.length > 0);
  const single = rules.text.titleSingleWordMinLength;
  const dictionaryWord = (word: string) => {
    const lower = word.toLowerCase();
    return lower.length >= single && /^[a-z]+$/.test(lower) && isKnownWord(filter, lower);
  };
  if (parts.length < 2 && !dictionaryWord(parts[0] ?? "")) {
    found.push(violation(FIELD, "needs_words", `Use at least two words, or one real word of ${single}+ letters.`));
  }

  const letters = title.match(/[A-Za-z]/g) ?? [];
  if (letters.length >= rules.text.titleAllCapsMinLetters && letters.every((c) => c >= "A" && c <= "Z")) {
    found.push(violation(FIELD, "all_caps", "Title must not be all capitals."));
  }
  if (title.length > rules.title.max) return found;
  if (hasLink(rules, title)) found.push(violation(FIELD, "contains_url", "Title must not contain a URL or domain."));

  const run = rules.text.titleMaxIdenticalRun;
  if (new RegExp(`(.)\\1{${run},}`, "i").test(title)) {
    found.push(violation(FIELD, "repeated_chars", `Title must not repeat a character more than ${run} times in a row.`));
  }
  const punct = rules.text.titleMaxPunctuationRun;
  if (new RegExp(`([!-/:-@\\[-\`{-~])\\1{${punct},}`).test(title)) {
    found.push(violation(FIELD, "repeated_punctuation", `Title must not repeat punctuation more than ${punct} times in a row.`));
  }
  return found;
}
