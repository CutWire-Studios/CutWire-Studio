import type { LengthRule, QualityRules, Violation } from "./types";
import { codePoint, countLinks, firstNonAscii, hasLink, normalizeLineEndings, violation } from "./text";

function capitalize(s: string): string {
  return s.charAt(0).toUpperCase() + s.slice(1);
}

function checkLength(found: Violation[], field: string, text: string, rule: LengthRule): void {
  if (text.trim().length === 0) {
    found.push(violation(field, "required", `${capitalize(field)} is required.`));
    return;
  }
  const bad = firstNonAscii(text, true);
  if (bad >= 0) {
    found.push(violation(field, "non_ascii", `${capitalize(field)} may only use printable ASCII characters.`, { index: bad, char: codePoint(text, bad) }));
  }
  if (text.length < rule.min) found.push(violation(field, "too_short", `${capitalize(field)} must be at least ${rule.min} characters.`, { min: rule.min }));
  if (text.length > rule.max) found.push(violation(field, "too_long", `${capitalize(field)} must be at most ${rule.max} characters.`, { max: rule.max }));
}

export function checkText(rules: QualityRules, rawSummary: string, rawDescription: string): Violation[] {
  const found: Violation[] = [];
  const summary = normalizeLineEndings(rawSummary);
  const description = normalizeLineEndings(rawDescription);

  checkLength(found, "summary", summary, rules.summary);
  if (hasLink(rules, summary)) found.push(violation("summary", "links_not_allowed", "Summary must not contain links."));

  checkLength(found, "description", description, rules.description);
  const links = countLinks(rules, description);
  if (links > rules.descriptionMaxLinks) {
    found.push(violation("description", "too_many_links", `Description may contain at most ${rules.descriptionMaxLinks} links.`, { max: rules.descriptionMaxLinks, count: links }));
  }
  if (new RegExp(`(\\n[ \\t]*){${rules.maxConsecutiveBlankLines + 2},}`).test(description)) {
    found.push(violation("description", "too_many_blank_lines", `Description may have at most ${rules.maxConsecutiveBlankLines} blank lines in a row.`, { max: rules.maxConsecutiveBlankLines }));
  }
  return found;
}
