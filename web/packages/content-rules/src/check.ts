import type { BloomFilter } from "./bloom";
import { checkText } from "./descriptive";
import { checkEnglish } from "./english";
import { checkProfanity } from "./profanity";
import { checkTaxonomy } from "./taxonomy";
import { checkTitle } from "./title";
import type { MetadataInput, QualityRules, Violation } from "./types";

/** Runs the same metadata gates as `ContentGatePipeline.CheckMetadataAsync` and returns every violation. */
export function checkMetadata(rules: QualityRules, filter: BloomFilter, input: MetadataInput): Violation[] {
  return [
    ...checkTitle(rules, filter, input.title),
    ...checkText(rules, input.summary, input.description),
    ...checkEnglish(rules, filter, input),
    ...checkProfanity(rules, input),
    ...checkTaxonomy(rules, input),
  ];
}
