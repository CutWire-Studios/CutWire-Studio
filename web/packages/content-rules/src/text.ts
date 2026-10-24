import type { QualityRules, Violation } from "./types";

export function violation(field: string, code: string, message: string, params?: Record<string, unknown>): Violation {
  return params ? { field, code, message, params } : { field, code, message };
}

export function linkRegex(rules: QualityRules): RegExp {
  return new RegExp(rules.text.linkPattern, "gi");
}

export function countLinks(rules: QualityRules, text: string): number {
  return text.match(linkRegex(rules))?.length ?? 0;
}

export function hasLink(rules: QualityRules, text: string): boolean {
  return countLinks(rules, text) > 0;
}

/** Index of the first character outside printable ASCII (newline allowed on request), or -1. */
export function firstNonAscii(text: string, allowNewline: boolean): number {
  for (let i = 0; i < text.length; i++) {
    const c = text.charCodeAt(i);
    if (c >= 0x20 && c <= 0x7e) continue;
    if (allowNewline && c === 0x0a) continue;
    return i;
  }
  return -1;
}

export function codePoint(text: string, index: number): string {
  return `U+${text.codePointAt(index)!.toString(16).toUpperCase().padStart(4, "0")}`;
}

export function normalizeLineEndings(text: string): string {
  return text.replaceAll("\r\n", "\n");
}

/** Lowercased alphanumeric words, an apostrophe suffix kept (matches `TextRules.Words`). */
export function words(text: string): string[] {
  return (text.match(/[A-Za-z0-9]+(?:'[A-Za-z]+)?/g) ?? []).map((w) => w.toLowerCase());
}
