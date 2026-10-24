import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { describe, expect, it } from "vitest";
import { BloomFilter, bloomHashes, bloomPositions, candidates, checkMetadata, fnv1a, murmur3 } from "../src";
import type { QualityRules } from "../src";

const root = resolve(import.meta.dirname, "..");
const fixture = <T>(name: string): T => JSON.parse(readFileSync(resolve(root, "test-fixtures", name), "utf8")) as T;
const bloomBytes = new Uint8Array(
  readFileSync(resolve(root, "../../../backend/src/CutWire.Studio.Application/Quality/Resources/english.bloom")),
);
const filter = BloomFilter.parse(bloomBytes);

interface BloomVectors {
  sha256: string;
  bitCount: number;
  hashCount: number;
  seed: number;
  wordCount: number;
  vectors: { word: string; h1: number; h2: number; positions: number[]; member: boolean }[];
  normalization: { token: string; candidates: string[]; known: boolean }[];
}

describe("hash functions", () => {
  const enc = (s: string) => new TextEncoder().encode(s);
  it("match published vectors", () => {
    expect(fnv1a(enc("hello"), 0)).toBe(0x4f9f2cab);
    expect(murmur3(enc("hello"), 0)).toBe(0x248bfa47);
    expect(murmur3(enc(""), 1)).toBe(0x514e28b7);
    expect(murmur3(enc("The quick brown fox jumps over the lazy dog"), 0x9747b28c)).toBe(0x2fa826cd);
  });
});

describe("bloom filter parity with the backend", () => {
  const vectors = fixture<BloomVectors>("bloom-vectors.json");

  it("reads the header", () => {
    expect(filter.bitCount).toBe(vectors.bitCount);
    expect(filter.hashCount).toBe(vectors.hashCount);
    expect(filter.seed).toBe(vectors.seed);
    expect(filter.wordCount).toBe(vectors.wordCount);
  });

  it.each(vectors.vectors)("hashes, positions and membership of %j agree", (v) => {
    const bytes = new TextEncoder().encode(v.word);
    const { h1, h2 } = bloomHashes(bytes, vectors.seed);
    expect(h1).toBe(v.h1);
    expect(h2).toBe(v.h2);
    expect(bloomPositions(bytes, vectors.bitCount, vectors.hashCount, vectors.seed)).toEqual(v.positions);
    expect(filter.mightContain(v.word)).toBe(v.member);
  });

  it.each(vectors.normalization)("suffix normalisation of $token agrees", (n) => {
    expect(candidates(n.token)).toEqual(n.candidates);
    expect(n.candidates.some((c) => filter.mightContain(c))).toBe(n.known);
  });

  it("rejects malformed files", () => {
    expect(() => BloomFilter.parse(bloomBytes.subarray(0, 10))).toThrow();
    expect(() => BloomFilter.parse(bloomBytes.subarray(0, bloomBytes.length - 1))).toThrow();
    const bad = bloomBytes.slice();
    bad[0] = 0x58;
    expect(() => BloomFilter.parse(bad)).toThrow();
  });
});

describe("metadata gates return the backend's codes", () => {
  const rules = fixture<QualityRules>("quality-rules.json");
  const cases = fixture<
    { name: string; kind: string; title: string; summary: string; description: string; tags: string[]; category: string; license: string; expected: string[] }[]
  >("gate-cases.json");

  it("ships the English word list that the rules point at", () => {
    expect(rules.englishWordList.url).toMatch(/^\/api\/v1\/meta\/english-[0-9a-f]{8}\.bloom$/);
    expect(rules.englishWordList.sha256).toBe(fixture<BloomVectors>("bloom-vectors.json").sha256);
  });

  it.each(cases)("$name", (c) => {
    const violations = checkMetadata(rules, filter, c);
    const actual = violations.map((v) => `${v.field}:${v.code}`).sort();
    expect(actual).toEqual(c.expected);
  });
});
