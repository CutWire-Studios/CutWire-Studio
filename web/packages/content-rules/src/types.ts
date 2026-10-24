export interface LengthRule {
  min: number;
  max: number;
}

export interface KindRules {
  id: string;
  displayName: string;
  acceptedExtensions: string[];
  maxPackageBytes: number;
  maxUnpackedBytes: number;
  categories: string[];
  tags: string[];
  media: { role: string; required: boolean; width: number; height: number; maxBytes: number; mimeType: string }[];
}

export interface TextRules {
  titleSingleWordMinLength: number;
  titleAllCapsMinLetters: number;
  titleMaxIdenticalRun: number;
  titleMaxPunctuationRun: number;
  linkPattern: string;
  techTerms: string[];
  profanity: string[];
}

export interface QualityRules {
  title: LengthRule;
  summary: LengthRule;
  description: LengthRule;
  descriptionMaxLinks: number;
  maxConsecutiveBlankLines: number;
  minTags: number;
  maxTags: number;
  licenses: string[];
  englishMinRatio: number;
  englishMinTokens: number;
  kinds: KindRules[];
  text: TextRules;
  englishWordList: { url: string; sha256: string; bytes: number; words: number; bits: number; hashCount: number; seed: number };
}

export interface Violation {
  field: string;
  code: string;
  message: string;
  params?: Record<string, unknown>;
}

export interface MetadataInput {
  kind: string;
  title: string;
  summary: string;
  description: string;
  tags: string[];
  category: string;
  license: string;
}
