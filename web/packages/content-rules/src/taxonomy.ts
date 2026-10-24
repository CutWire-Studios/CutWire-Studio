import type { MetadataInput, QualityRules, Violation } from "./types";
import { violation } from "./text";

export function checkTaxonomy(rules: QualityRules, input: MetadataInput): Violation[] {
  const kind = rules.kinds.find((k) => k.id === input.kind);
  if (!kind) return [violation("kind", "unknown_kind", "Unknown or unavailable asset kind.")];

  const found: Violation[] = [];
  if (input.category.trim().length === 0) found.push(violation("category", "required", "Category is required."));
  else if (!kind.categories.includes(input.category)) {
    found.push(violation("category", "invalid_category", "Category is not valid for this kind.", { allowed: kind.categories }));
  }

  const tags = input.tags;
  if (tags.length < rules.minTags) found.push(violation("tags", "too_few_tags", `Pick at least ${rules.minTags} tag.`, { min: rules.minTags }));
  if (tags.length > rules.maxTags) found.push(violation("tags", "too_many_tags", `Pick at most ${rules.maxTags} tags.`, { max: rules.maxTags }));
  for (const tag of new Set(tags.filter((t) => !kind.tags.includes(t)))) {
    found.push(violation("tags", "unknown_tag", `Tag "${tag}" is not in the vocabulary.`, { tag }));
  }
  const seen = new Map<string, { first: string; count: number }>();
  for (const tag of tags) {
    const key = tag.toLowerCase();
    const entry = seen.get(key);
    if (entry) entry.count++;
    else seen.set(key, { first: tag, count: 1 });
  }
  for (const { first, count } of seen.values()) {
    if (count > 1) found.push(violation("tags", "duplicate_tag", `Tag "${first}" is listed more than once.`, { tag: first }));
  }

  if (input.license.trim().length === 0) found.push(violation("license", "required", "License is required."));
  else if (!rules.licenses.includes(input.license)) {
    found.push(violation("license", "invalid_license", "License is not one of the allowed SPDX identifiers.", { allowed: rules.licenses }));
  }
  return found;
}
