import type { MetadataInput, QualityRules, Violation } from "./types";
import { violation } from "./text";

const LEET: Record<string, string> = { "0": "o", "1": "i", "3": "e", "4": "a", "5": "s", "@": "a", $: "s" };

function deleet(token: string): string {
  return Array.from(token.toLowerCase(), (c) => LEET[c] ?? c).join("");
}

export function containsProfanity(rules: QualityRules, text: string | undefined): boolean {
  if (!text) return false;
  const deny = new Set(rules.text.profanity);
  return (text.match(/[A-Za-z0-9@$]+/g) ?? []).some((token) => deny.has(deleet(token)));
}

export function checkProfanity(rules: QualityRules, input: MetadataInput): Violation[] {
  const fields: [string, string][] = [
    ["title", input.title],
    ["summary", input.summary],
    ["description", input.description],
    ["tags", input.tags.join(" ")],
  ];
  return fields
    .filter(([, text]) => containsProfanity(rules, text))
    .map(([field]) => violation(field, "profanity", "This text contains language that is not allowed."));
}
